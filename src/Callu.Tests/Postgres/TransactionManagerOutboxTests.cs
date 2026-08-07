using Callu.Application.Common.Interfaces;
using Callu.Application.Contracts.Messages;
using Callu.Domain.Entities;
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

/// <summary>The transactional context runs the operation exactly once, and a rollback on it leaves the scope reusable.</summary>
[Collection(PostgresCollection.Name)]
public class TransactionManagerOutboxTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task TheTransactionalContext_DoesNotRetry_SoTheOperationIsNeverReplayed()
    {
        var cs = await pg.CreateDatabaseAsync();
        await using var provider = BuildApp(cs);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(
            db.Database.CreateExecutionStrategy().RetriesOnFailure,
            "A retrying strategy replays the transaction body on a DbContext that still holds the "
            + "failed attempt's tracked entities, so the second run stages everything twice.");

        // With a retrying strategy configured this throws ("does not support user-initiated
        // transactions"), which is what forces the transaction body through a replayable lambda.
        await using var tx = await db.Database.BeginTransactionAsync();
        await tx.RollbackAsync();
    }

    /// <summary>Factory contexts do retry, because nobody shares them and the callback writes have no transaction.</summary>
    // A voice callback is a responder pressing "1"; the provider never retries it, so a hiccup loses the ack.
    [PostgresFact]
    public async Task FactoryContextsRetry_EvenThoughTheTransactionalOneMustNot()
    {
        var cs = await pg.CreateDatabaseAsync();
        await using var provider = BuildApp(cs);
        using var scope = provider.CreateScope();

        var scoped = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(
            scoped.Database.CreateExecutionStrategy().RetriesOnFailure,
            "the transactional context carries staged messages; a replay stages them a second time.");

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var fromFactory = await factory.CreateDbContextAsync();
        Assert.True(
            fromFactory.Database.CreateExecutionStrategy().RetriesOnFailure,
            "a transient database error must not throw away a responder's phone acknowledgement.");
    }

    [PostgresFact]
    public async Task RolledBackTransaction_LeavesNothingBehind_ForTheNextTransactionOnTheSameScope()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var provider = BuildApp(cs);
        using var scope = provider.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transactions.ExecuteInTransactionAsync(async () =>
            {
                await db.Incidents.AddAsync(NewIncident("Rolled back"));
                throw new InvalidOperationException("boom");
            }));

        await transactions.ExecuteInTransactionAsync(async () =>
        {
            await db.Incidents.AddAsync(NewIncident("Committed"));
        });

        Assert.Equal(1L, await ScalarAsync(cs, """SELECT COUNT(*) FROM "Incidents" """));
        Assert.Equal(1L, await ScalarAsync(
            cs, """SELECT COUNT(*) FROM "Incidents" WHERE "Title" = 'Committed'"""));
    }

    // ---- the rollback path is where a staged message used to be orphaned ------

    /// <summary>A message staged after a rolled-back one, on the same scope, commits exactly once.</summary>
    [PostgresFact]
    public async Task AMessageStagedAfterARolledBackOne_OnTheSameScope_CommitsExactlyOnce()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var provider = BuildApp(cs);
        using var scope = provider.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var lostIncidentId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transactions.ExecuteInTransactionAsync(async () =>
            {
                var incident = NewIncident("Rolled back");
                incident.Id = lostIncidentId;
                await db.Incidents.AddAsync(incident);
                outbox.Stage(
                    CalluTopology.TriggerIncidentEscalationMessage,
                    new TriggerIncidentEscalation(lostIncidentId, Guid.NewGuid()));

                throw new InvalidOperationException("boom");
            }));

        var keptIncidentId = Guid.NewGuid();
        await transactions.ExecuteInTransactionAsync(async () =>
        {
            var incident = NewIncident("Committed");
            incident.Id = keptIncidentId;
            await db.Incidents.AddAsync(incident);
            outbox.Stage(
                CalluTopology.TriggerIncidentEscalationMessage,
                new TriggerIncidentEscalation(keptIncidentId, Guid.NewGuid()));
        });

        // The rolled-back write and its message are gone; the committed one is there, exactly once.
        Assert.Equal(1L, await ScalarAsync(cs, """SELECT COUNT(*) FROM "Incidents" """));
        Assert.Equal(1L, await ScalarAsync(cs, """SELECT COUNT(*) FROM "OutboxEntries" """));
        Assert.Equal(1L, await ScalarAsync(
            cs, $"""SELECT COUNT(*) FROM "OutboxEntries" WHERE "Payload" LIKE '%{keptIncidentId}%'"""));
    }

    /// <summary>Commit, rollback, commit on one scope: two messages, and no attempt to re-insert the first.</summary>
    [PostgresFact]
    public async Task ARollbackBetweenTwoCommits_DoesNotDisturbTheMessageAlreadyWritten()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var provider = BuildApp(cs);
        using var scope = provider.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await transactions.ExecuteInTransactionAsync(async () =>
        {
            await db.Incidents.AddAsync(NewIncident("First"));
            outbox.Stage(
                CalluTopology.TriggerIncidentEscalationMessage,
                new TriggerIncidentEscalation(Guid.NewGuid(), Guid.NewGuid()));
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transactions.ExecuteInTransactionAsync(async () =>
            {
                await db.Incidents.AddAsync(NewIncident("Rolled back"));
                outbox.Stage(
                    CalluTopology.TriggerIncidentEscalationMessage,
                    new TriggerIncidentEscalation(Guid.NewGuid(), Guid.NewGuid()));
                throw new InvalidOperationException("boom");
            }));

        await transactions.ExecuteInTransactionAsync(async () =>
        {
            await db.Incidents.AddAsync(NewIncident("Third"));
            outbox.Stage(
                CalluTopology.TriggerIncidentEscalationMessage,
                new TriggerIncidentEscalation(Guid.NewGuid(), Guid.NewGuid()));
        });

        Assert.Equal(2L, await ScalarAsync(cs, """SELECT COUNT(*) FROM "Incidents" """));
        Assert.Equal(2L, await ScalarAsync(cs, """SELECT COUNT(*) FROM "OutboxEntries" """));
    }

    /// <summary>A transaction that stages nothing but the message still commits it.</summary>
    [PostgresFact]
    public async Task PublishOnlyTransaction_StillCommitsItsMessage()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var provider = BuildApp(cs);
        using var scope = provider.CreateScope();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        var statusPageIncidentId = Guid.NewGuid();

        await transactions.ExecuteInTransactionAsync(() =>
        {
            outbox.Stage(
                CalluTopology.NotifyStatusPageSubscribersMessage,
                new NotifyStatusPageSubscribers(statusPageIncidentId));

            return Task.CompletedTask;
        });

        Assert.Equal(1L, await ScalarAsync(
            cs, $"""SELECT COUNT(*) FROM "OutboxEntries" WHERE "Payload" LIKE '%{statusPageIncidentId}%'"""));
    }

    // ---- harness ------------------------------------------------------------

    private static Incident NewIncident(string title) =>
        new() { Title = title, StartedAt = DateTime.UtcNow };

    /// <summary>
    /// The production persistence wiring, so the no-retry decision is asserted where it is made
    /// rather than restated by the test.
    /// </summary>
    private static ServiceProvider BuildApp(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        return services.BuildServiceProvider();
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
