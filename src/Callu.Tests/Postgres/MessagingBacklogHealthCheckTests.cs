using Callu.Application.Common.Interfaces;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Messaging.Health;
using Callu.Infrastructure.Messaging.Persistence;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace Callu.Tests;

/// <summary>A broker that is up while messages pile up behind it is the outage this check exists to show.</summary>
[Collection(PostgresCollection.Name)]
public class MessagingBacklogHealthCheckTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task WithNothingStaged_TheCountsAreZero()
    {
        var data = await CheckAsync(await FreshDatabaseAsync(), HealthStatus.Healthy);

        Assert.Equal(0, data["outboxWaiting"]);
        Assert.Equal(0, data["outboxUndelivered"]);
        Assert.Equal(0, data["inboxRetrying"]);
        Assert.Equal(0, data["inboxAbandoned"]);
    }

    /// <summary>Sent and Consumed rows are done; counting them would report a permanent phantom backlog.</summary>
    [PostgresFact]
    public async Task OnlyTheRowsStillOwedSomething_AreCounted()
    {
        var cs = await FreshDatabaseAsync();

        await SeedAsync(cs, db =>
        {
            db.OutboxEntries.Add(Outbox(OutboxEntryStatus.Pending));
            db.OutboxEntries.Add(Outbox(OutboxEntryStatus.Claimed));
            db.OutboxEntries.Add(Outbox(OutboxEntryStatus.Failed));
            db.OutboxEntries.Add(Outbox(OutboxEntryStatus.Sent));
            db.OutboxEntries.Add(Outbox(OutboxEntryStatus.Sent));

            db.InboxEntries.Add(Inbox(InboxEntryStatus.Retrying));
            db.InboxEntries.Add(Inbox(InboxEntryStatus.Retrying));
            db.InboxEntries.Add(Inbox(InboxEntryStatus.Failed));
            db.InboxEntries.Add(Inbox(InboxEntryStatus.Consumed));
        });

        var data = await CheckAsync(cs, HealthStatus.Healthy);

        Assert.Equal(2, data["outboxWaiting"]);
        Assert.Equal(1, data["outboxUndelivered"]);
        Assert.Equal(2, data["inboxRetrying"]);
        Assert.Equal(1, data["inboxAbandoned"]);
    }

    /// <summary>
    /// A backlog is a number to read, not a gate. Reporting anything but Healthy here would take a
    /// host off readiness for messages that are still going to be delivered.
    /// </summary>
    [PostgresFact]
    public async Task ABacklogIsReported_ButNeverAsAFailure()
    {
        var cs = await FreshDatabaseAsync();

        await SeedAsync(cs, db =>
        {
            for (var i = 0; i < 250; i++) db.OutboxEntries.Add(Outbox(OutboxEntryStatus.Pending));
        });

        var result = await RunAsync(cs);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(250, result.Data["outboxWaiting"]);
        Assert.Contains("250", result.Description ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Not knowing the backlog is not the same as the messaging path being broken.</summary>
    [PostgresFact]
    public async Task AnUnreadableDatabase_IsDegraded_NotUnhealthy()
    {
        // A database that exists but has no tables: the connection opens, the count cannot run.
        var result = await RunAsync(await pg.CreateDatabaseAsync());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.NotNull(result.Exception);
    }

    private async Task<string> FreshDatabaseAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        return cs;
    }

    private static async Task<IReadOnlyDictionary<string, object>> CheckAsync(string cs, HealthStatus expected)
    {
        var result = await RunAsync(cs);
        Assert.Equal(expected, result.Status);
        return result.Data;
    }

    private static async Task<HealthCheckResult> RunAsync(string connectionString)
    {
        await using var provider = BuildApp(connectionString);
        var check = new MessagingBacklogHealthCheck(
            provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>());

        return await check.CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration(
                    MessagingBacklogHealthCheck.Name, check, HealthStatus.Unhealthy, tags: null),
            },
            CancellationToken.None);
    }

    private static async Task SeedAsync(string connectionString, Action<ApplicationDbContext> seed)
    {
        await using var provider = BuildApp(connectionString);
        await using var db = await provider
            .GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();

        seed(db);
        await db.SaveChangesAsync();
    }

    private static OutboxEntry Outbox(OutboxEntryStatus status) => new()
    {
        MessageType = "incident.escalation.trigger.v1",
        Payload = "{}",
        Status = status,
        NextAttemptAt = DateTime.UtcNow,
        SentAt = status == OutboxEntryStatus.Sent ? DateTime.UtcNow : null,
    };

    private static InboxEntry Inbox(InboxEntryStatus status) => new()
    {
        MessageType = "incident.escalation.trigger.v1",
        Payload = "{}",
        Status = status,
        NextAttemptAt = DateTime.UtcNow,
        ConsumedAt = status == InboxEntryStatus.Consumed ? DateTime.UtcNow : null,
    };

    private static ServiceProvider BuildApp(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);

        return services.BuildServiceProvider();
    }
}
