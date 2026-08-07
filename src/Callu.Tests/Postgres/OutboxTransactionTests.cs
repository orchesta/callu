using Callu.Application.Common.Interfaces;
using Callu.Application.Contracts.Messages;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Messaging.Outbox;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The staged message and the domain write commit together, or neither of them does.</summary>
[Collection(PostgresCollection.Name)]
public class OutboxTransactionTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task StagedMessage_CommitsWithTheDomainWrite_AndNotBefore()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using var provider = BuildApp(cs);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        var insideTransaction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var work = transactions.ExecuteInTransactionAsync(async () =>
        {
            db.Incidents.Add(NewIncident());
            outbox.Stage(
                CalluTopology.TriggerIncidentEscalationMessage,
                new TriggerIncidentEscalation(Guid.NewGuid(), Guid.NewGuid()));

            // Hold the transaction open and let a second connection look.
            insideTransaction.SetResult();
            await released.Task;
            return true;
        });

        await insideTransaction.Task;

        Assert.Equal(0L, await CountAsync(cs, "Incidents"));
        Assert.Equal(0L, await CountAsync(cs, "OutboxEntries"));

        released.SetResult();
        await work;

        Assert.Equal(1L, await CountAsync(cs, "Incidents"));
        Assert.Equal(1L, await CountAsync(cs, "OutboxEntries"));
    }

    [PostgresFact]
    public async Task ARolledBackTransaction_LeavesNoStagedMessage()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using var provider = BuildApp(cs);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transactions.ExecuteInTransactionAsync<bool>(() =>
            {
                db.Incidents.Add(NewIncident());
                outbox.Stage(
                    CalluTopology.TriggerIncidentEscalationMessage,
                    new TriggerIncidentEscalation(Guid.NewGuid(), Guid.NewGuid()));

                throw new InvalidOperationException("the domain write failed after staging");
            }));

        Assert.Equal(0L, await CountAsync(cs, "Incidents"));
        Assert.Equal(0L, await CountAsync(cs, "OutboxEntries"));
    }

    /// <summary>Staging outside a transaction would let the message commit apart from the domain write.</summary>
    [PostgresFact]
    public async Task StagingOutsideATransaction_Throws()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using var provider = BuildApp(cs);

        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        Assert.Throws<InvalidOperationException>(() => outbox.Stage(
            CalluTopology.TriggerIncidentEscalationMessage,
            new TriggerIncidentEscalation(Guid.NewGuid(), Guid.NewGuid())));

        Assert.Equal(0L, await CountAsync(cs, "OutboxEntries"));
    }

    /// <summary>An unknown wire name is refused before anything else, so a typo cannot stage an undeliverable row.</summary>
    [PostgresFact]
    public async Task AMessageTypeWithNoRoutingKey_IsRefusedAtStagingTime()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using var provider = BuildApp(cs);

        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        var thrown = Assert.Throws<ArgumentException>(() => outbox.Stage("not.a.known.message", new { }));
        Assert.Contains("not.a.known.message", thrown.Message, StringComparison.Ordinal);
    }

    private static Incident NewIncident() => new()
    {
        Title = "outbox staging",
        Severity = IncidentSeverity.High,
        Status = IncidentStatus.Open,
        StartedAt = DateTime.UtcNow,
    };

    private static async Task<long> CountAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""SELECT COUNT(*) FROM "{table}" """, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static ServiceProvider BuildApp(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        return services.BuildServiceProvider();
    }
}
