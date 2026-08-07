using System.Text.Json;
using Callu.Application.Common.Interfaces;
using Callu.Application.Contracts.Messages;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Messaging.Consuming;
using Callu.Infrastructure.Messaging.Persistence;
using Callu.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The sweep owns the ladder: it re-runs a failed delivery from its row, and gives up on the record.</summary>
[Collection(PostgresCollection.Name)]
public class InboxRetrySweepTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task ADueRow_IsRerun_AndItsWritesCommitWithTheRowsNewState()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var probe = new SweepProbe { Throw = true };
        await using var provider = BuildApp(cs, probe);

        // First delivery fails and leaves a row that owes a retry.
        var messageId = Guid.NewGuid();
        await provider.GetRequiredService<IInboxMessageProcessor>()
            .ProcessAsync(messageId, SweepHandler.Wire, Payload(), null, CancellationToken.None);

        await MakeDueAsync(cs, messageId);
        probe.Throw = false;

        var handled = await provider.GetRequiredService<IInboxRetrySweep>().SweepAsync(CancellationToken.None);

        Assert.Equal(1, handled);
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" WHERE "Status" = 'Consumed' """));
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "AuditLogs" WHERE "ResourceType" = 'inbox-sweep-test' """));
    }

    [PostgresFact]
    public async Task ARowThatKeepsFailing_GoesTerminalAtTheEntitysBound()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var probe = new SweepProbe { Throw = true };
        await using var provider = BuildApp(cs, probe);
        var sweep = provider.GetRequiredService<IInboxRetrySweep>();

        var messageId = Guid.NewGuid();
        await provider.GetRequiredService<IInboxMessageProcessor>()
            .ProcessAsync(messageId, SweepHandler.Wire, Payload(), null, CancellationToken.None);

        for (var attempt = 0; attempt < InboxEntry.MaxAttempts + 2; attempt++)
        {
            await MakeDueAsync(cs, messageId);
            await sweep.SweepAsync(CancellationToken.None);
        }

        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" WHERE "Status" = 'Failed' """));
        Assert.Equal(
            InboxEntry.MaxAttempts,
            (int)await CountAsync(cs, $"""SELECT "AttemptCount"::bigint FROM "InboxEntries" WHERE "Id" = '{messageId}' """));

        // Nothing the handler tried to write survives a run that ended in failure.
        Assert.Equal(0L, await CountAsync(cs, """SELECT COUNT(*) FROM "AuditLogs" WHERE "ResourceType" = 'inbox-sweep-test' """));

        // But giving up is reported once, where an operator looks.
        Assert.Equal(1, probe.PermanentFailures);
    }

    [PostgresFact]
    public async Task ARowThatIsNotDueYet_IsLeftAlone()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var probe = new SweepProbe { Throw = true };
        await using var provider = BuildApp(cs, probe);

        await provider.GetRequiredService<IInboxMessageProcessor>()
            .ProcessAsync(Guid.NewGuid(), SweepHandler.Wire, Payload(), null, CancellationToken.None);

        // The first failure pushed NextAttemptAt into the future, so this sweep must find nothing.
        Assert.Equal(0, await provider.GetRequiredService<IInboxRetrySweep>().SweepAsync(CancellationToken.None));
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" WHERE "Status" = 'Retrying' """));
    }

    private static string Payload() => JsonSerializer.Serialize(
        new NotifyStatusPageSubscribers(Guid.NewGuid()),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task MakeDueAsync(string connectionString, Guid messageId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""UPDATE "InboxEntries" SET "NextAttemptAt" = NOW() - INTERVAL '1 minute' WHERE "Id" = '{messageId}'""",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static ServiceProvider BuildApp(string connectionString, SweepProbe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);
        services.AddSingleton(probe);
        services.AddScoped<ICalluMessageHandler, SweepHandler>();
        services.AddScoped<IInboxMessageProcessor, InboxMessageProcessor>();
        services.AddScoped<IInboxRetrySweep, InboxRetrySweep>();

        return services.BuildServiceProvider();
    }

    private sealed class SweepProbe
    {
        private int _permanentFailures;

        public bool Throw { get; set; }

        public int PermanentFailures => _permanentFailures;

        public void RecordPermanentFailure() => Interlocked.Increment(ref _permanentFailures);
    }

    private sealed class SweepHandler(ApplicationDbContext db, SweepProbe probe) : ICalluMessageHandler
    {
        public const string Wire = CalluTopology.NotifyStatusPageSubscribersMessage;

        public string WireName => Wire;

        public Task HandleAsync(string payload, CancellationToken cancellationToken)
        {
            if (probe.Throw) throw new InvalidOperationException("handler failed");

            db.AuditLogs.Add(new AuditLog
            {
                Action = AuditAction.Created,
                ResourceType = "inbox-sweep-test",
                Summary = "message.handled",
            });

            return Task.CompletedTask;
        }

        public Task ReportPermanentFailureAsync(string payload, string reason, CancellationToken cancellationToken)
        {
            probe.RecordPermanentFailure();
            return Task.CompletedTask;
        }
    }
}
