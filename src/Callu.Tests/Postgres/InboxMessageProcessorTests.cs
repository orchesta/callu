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

/// <summary>The consume sequence: the dedupe row and the handler's writes commit together, then the ack.</summary>
[Collection(PostgresCollection.Name)]
public class InboxMessageProcessorTests(PostgresFixture pg)
{
    /// <summary>The failure this pins is silent: an inbox row committed while the handler's writes are dropped.</summary>
    [PostgresFact]
    public async Task AHandlersWrites_CommitWithTheInboxRow()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var probe = new HandlerProbe();
        await using var provider = BuildApp(cs, probe);

        var outcome = await provider.GetRequiredService<IInboxMessageProcessor>().ProcessAsync(
            Guid.NewGuid(), ProbeHandler.Wire, Payload(), traceParent: null, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Ack, outcome);
        Assert.Equal(1, probe.Calls);

        // From a second connection: the dedupe row and what the handler wrote are both visible.
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" """));
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "AuditLogs" WHERE "ResourceType" = 'inbox-processor-test' """));
    }

    [PostgresFact]
    public async Task ASecondDeliveryOfTheSameMessage_IsAckedWithoutRunningTheHandlerAgain()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var probe = new HandlerProbe();
        await using var provider = BuildApp(cs, probe);
        var processor = provider.GetRequiredService<IInboxMessageProcessor>();

        var messageId = Guid.NewGuid();
        await processor.ProcessAsync(messageId, ProbeHandler.Wire, Payload(), null, CancellationToken.None);
        var second = await processor.ProcessAsync(messageId, ProbeHandler.Wire, Payload(), null, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Ack, second);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" """));
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "AuditLogs" WHERE "ResourceType" = 'inbox-processor-test' """));
    }

    /// <summary>A failed handler leaves a retryable row and acks, so a persistent failure cannot hot-loop the broker.</summary>
    [PostgresFact]
    public async Task AFailedHandler_LeavesARetryableRow_RatherThanRequeueing()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var probe = new HandlerProbe { Throw = true };
        await using var provider = BuildApp(cs, probe);

        var outcome = await provider.GetRequiredService<IInboxMessageProcessor>().ProcessAsync(
            Guid.NewGuid(), ProbeHandler.Wire, Payload(), null, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Ack, outcome);

        // Nothing the handler wrote survives, but the delivery is on record as owing a retry.
        Assert.Equal(0L, await CountAsync(cs, """SELECT COUNT(*) FROM "AuditLogs" """));
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" WHERE "Status" = 'Retrying' AND "AttemptCount" = 1 """));
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" WHERE "NextAttemptAt" > NOW() """));
        Assert.Equal(1L, await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" WHERE "LastError" IS NOT NULL """));
    }

    /// <summary>A redelivery of a row that owes a retry is acked untouched: the sweep owns the ladder, not the broker.</summary>
    [PostgresFact]
    public async Task ARedeliveryOfARowThatOwesARetry_IsAckedWithoutAdvancingTheLadder()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var probe = new HandlerProbe { Throw = true };
        await using var provider = BuildApp(cs, probe);
        var processor = provider.GetRequiredService<IInboxMessageProcessor>();

        var messageId = Guid.NewGuid();
        await processor.ProcessAsync(messageId, ProbeHandler.Wire, Payload(), null, CancellationToken.None);
        var redelivery = await processor.ProcessAsync(messageId, ProbeHandler.Wire, Payload(), null, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Ack, redelivery);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(
            1L,
            await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" WHERE "Status" = 'Retrying' AND "AttemptCount" = 1 """));
    }

    [PostgresFact]
    public async Task AMessageTypeWithNoHandler_IsDeadLettered()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var provider = BuildApp(cs, new HandlerProbe());

        var outcome = await provider.GetRequiredService<IInboxMessageProcessor>().ProcessAsync(
            Guid.NewGuid(), "nobody.handles.this", Payload(), null, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.DeadLetter, outcome);
        Assert.Equal(0L, await CountAsync(cs, """SELECT COUNT(*) FROM "InboxEntries" """));
    }

    private static string Payload() => JsonSerializer.Serialize(
        new NotifyStatusPageSubscribers(Guid.NewGuid()),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static ServiceProvider BuildApp(string connectionString, HandlerProbe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);
        services.AddSingleton(probe);
        services.AddScoped<ICalluMessageHandler, ProbeHandler>();
        services.AddScoped<IInboxMessageProcessor, InboxMessageProcessor>();

        return services.BuildServiceProvider();
    }

    private sealed class HandlerProbe
    {
        private int _calls;

        public int Calls => _calls;

        public bool Throw { get; init; }

        public void Record() => Interlocked.Increment(ref _calls);
    }

    /// <summary>Writes through the processor's own scoped context and never flushes, which is the point.</summary>
    private sealed class ProbeHandler(ApplicationDbContext db, HandlerProbe probe) : ICalluMessageHandler
    {
        public const string Wire = CalluTopology.NotifyStatusPageSubscribersMessage;

        public const string HandledMarker = "inbox-processor-test";

        public string WireName => Wire;

        public Task HandleAsync(string payload, CancellationToken cancellationToken)
        {
            probe.Record();
            if (probe.Throw) throw new InvalidOperationException("handler failed");

            db.AuditLogs.Add(new AuditLog
            {
                Action = AuditAction.Created,
                ResourceType = HandledMarker,
                Summary = "message.handled",
            });

            return Task.CompletedTask;
        }
    }
}
