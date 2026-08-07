using System.Text.Json;
using Callu.Application.Common.Interfaces;
using Callu.Application.Contracts.Messages;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Messaging.Broker;
using Callu.Infrastructure.Messaging.Consuming;
using Callu.Infrastructure.Messaging.Outbox;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The whole loop against a real broker: staged in a transaction, published, consumed, committed.</summary>
[Collection(RabbitMqCollection.Name)]
public class CalluTransportEndToEndTests(RabbitMqFixture rabbit, PostgresFixture pg)
{
    [RabbitMqFact]
    public async Task AStagedMessage_ReachesTheHandler_AndIsMarkedSent()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = BuildBothHosts(cs, handled);

        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted) await service.StartAsync(CancellationToken.None);

        try
        {
            var messageId = await StageAsync(provider);

            // The handler signals from inside its transaction, so the rows are asserted by waiting for the
            // commit rather than by racing it.
            await handled.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // The publisher only writes Sent after the broker confirmed it.
            await WaitForAsync(cs, async () =>
                await ScalarAsync(cs, $"""SELECT COUNT(*) FROM "OutboxEntries" WHERE "Id" = '{messageId}' AND "Status" = 'Sent' AND "SentAt" IS NOT NULL """) == 1L);

            await WaitForAsync(cs, async () =>
                await ScalarAsync(cs, $"""SELECT COUNT(*) FROM "InboxEntries" WHERE "Id" = '{messageId}' AND "Status" = 'Consumed' """) == 1L);

            await WaitForAsync(cs, async () =>
                await ScalarAsync(cs, """SELECT COUNT(*) FROM "AuditLogs" WHERE "ResourceType" = 'transport-e2e' """) >= 1L);
        }
        finally
        {
            foreach (var service in Enumerable.Reverse(hosted)) await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>The broker being down must not lose the message: it waits in the outbox and goes later.</summary>
    [RabbitMqFact]
    public async Task AMessageStagedWhileTheBrokerIsUnreachable_IsPublishedOnceItIsBack()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        // Staged with a publisher pointed at nothing: the row is written, the send cannot happen.
        Guid messageId;
        await using (var broken = BuildPublisher(cs, host: "a-host-that-does-not-resolve.invalid"))
        {
            messageId = await StageAsync(broken);
            var dispatcher = broken.GetRequiredService<OutboxDispatcher>();

            await dispatcher.StartAsync(CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(2));
            await dispatcher.StopAsync(CancellationToken.None);
        }

        Assert.Equal(1L, await ScalarAsync(cs, $"""SELECT COUNT(*) FROM "OutboxEntries" WHERE "Id" = '{messageId}' AND "Status" <> 'Sent' """));

        // A failed attempt leaves the claim in place until its lease runs out, and the retry ladder
        // pushes the next attempt out on top of that. Both are deliberate, and both are somebody
        // else's test: what this one is about is the row still being there and going out once the
        // broker answers, so it is put back in reach rather than raced against the clock.
        Assert.Equal(1L, await ScalarAsync(cs, $"""
            WITH released AS (
              UPDATE "OutboxEntries"
              SET "ClaimedUntil" = NULL, "NextAttemptAt" = NOW()
              WHERE "Id" = '{messageId}'
              RETURNING 1)
            SELECT COUNT(*) FROM released
            """));

        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var working = BuildBothHosts(cs, handled);
        var hosted = working.GetServices<IHostedService>().ToList();
        foreach (var service in hosted) await service.StartAsync(CancellationToken.None);

        try
        {
            await handled.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await WaitForAsync(cs, async () =>
                await ScalarAsync(cs, $"""SELECT COUNT(*) FROM "OutboxEntries" WHERE "Id" = '{messageId}' AND "Status" = 'Sent' """) == 1L);
        }
        finally
        {
            foreach (var service in Enumerable.Reverse(hosted)) await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Stages one message and returns its id, so every assertion can name its own row.</summary>
    // Both tests in this collection publish through the same broker and the same queues, so a count
    // over the whole table also counts the other test's message. Assert on the id, not on the total.
    private static async Task<Guid> StageAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        return await transactions.ExecuteInTransactionAsync(() =>
        {
            db.Incidents.Add(new Incident
            {
                Title = "end to end",
                Severity = IncidentSeverity.High,
                Status = IncidentStatus.Open,
                StartedAt = DateTime.UtcNow,
            });

            var id = outbox.Stage(
                CalluTopology.NotifyStatusPageSubscribersMessage,
                new NotifyStatusPageSubscribers(Guid.NewGuid()));

            return Task.FromResult(id);
        });
    }

    private static async Task WaitForAsync(string connectionString, Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(200);
        }

        Assert.Fail("The condition never became true within 30 seconds. " + await DescribeRowsAsync(connectionString));
    }

    /// <summary>A timed-out wait has to say what the rows actually are, or the failure is undiagnosable.</summary>
    private static async Task<string> DescribeRowsAsync(string connectionString)
    {
        var lines = new List<string>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var table in (string[])["OutboxEntries", "InboxEntries"])
        {
            await using var command = new NpgsqlCommand(
                $"""SELECT "Status", "AttemptCount", COALESCE("LastError", '') FROM "{table}" """, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                lines.Add($"{table}: {reader.GetString(0)} attempts={reader.GetInt32(1)} error={reader.GetString(2)}");
        }

        return lines.Count == 0 ? "No outbox or inbox rows at all." : string.Join(" | ", lines);
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private ServiceProvider BuildPublisher(string connectionString, string? host = null)
    {
        var services = Base(connectionString);
        services.AddSingleton(Settings(host ?? rabbit.Host));
        services.AddSingleton<ICalluBrokerConnection, CalluBrokerConnection>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<IOutboxNudge>(sp => sp.GetRequiredService<OutboxDispatcher>());

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildBothHosts(string connectionString, TaskCompletionSource handled)
    {
        var services = Base(connectionString);
        services.AddSingleton(Settings(rabbit.Host));
        services.AddSingleton<ICalluBrokerConnection, CalluBrokerConnection>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        services.AddSingleton<OutboxDispatcher>();
        services.AddSingleton<IOutboxNudge>(sp => sp.GetRequiredService<OutboxDispatcher>());
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

        services.AddSingleton(handled);
        services.AddScoped<ICalluMessageHandler, EndToEndHandler>();
        services.AddScoped<IInboxMessageProcessor, InboxMessageProcessor>();
        services.AddHostedService<CalluConsumerHost>();

        return services.BuildServiceProvider();
    }

    private RabbitMqSettings Settings(string host) => new()
    {
        Host = host,
        Port = host == rabbit.Host ? rabbit.Port : null,
        Username = rabbit.Username,
        Password = rabbit.Password,
        VirtualHost = "/",
    };

    private static ServiceCollection Base(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);
        return services;
    }

    private sealed class EndToEndHandler(ApplicationDbContext db, TaskCompletionSource handled) : ICalluMessageHandler
    {
        public string WireName => CalluTopology.NotifyStatusPageSubscribersMessage;

        public Task HandleAsync(string payload, CancellationToken cancellationToken)
        {
            JsonSerializer.Deserialize<NotifyStatusPageSubscribers>(
                payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            db.AuditLogs.Add(new AuditLog
            {
                Action = AuditAction.Created,
                ResourceType = "transport-e2e",
                Summary = "handled over the wire",
            });

            handled.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
