using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.BackgroundJobs;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>What <see cref="NotificationReaperBackgroundService"/> will and will not reclaim.</summary>
// It is the only thing that moves a row stranded in Sending or Retrying, which the retry sweep never revisits.
[Collection(PostgresCollection.Name)]
public class NotificationReaperTests(PostgresFixture pg)
{
    /// <summary>Older than the reaper's 10-minute StuckAfter: this row's send never came back.</summary>
    private static readonly TimeSpan Stranded = TimeSpan.FromMinutes(15);

    /// <summary>Inside the window: a send that is legitimately still in flight and must be left alone.</summary>
    private static readonly TimeSpan MidSend = TimeSpan.FromMinutes(1);

    /// <summary>A stranded row with budget left is reclaimed; a send still in flight is left alone.</summary>
    [PostgresFact]
    public async Task AStrandedRowWithBudgetLeft_IsReclaimedForRetry_AndAFreshSendIsLeftAlone()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var stranded = await SeedAsync(cs, NotificationDeliveryStatus.Retrying, retryCount: 1, attemptedAgo: Stranded);
        var midSend = await SeedAsync(cs, NotificationDeliveryStatus.Sending, retryCount: 0, attemptedAgo: MidSend);

        await Reaper(cs).ReclaimStuckAsync(CancellationToken.None);

        var reclaimed = await ReloadAsync(cs, stranded);
        Assert.Equal(NotificationDeliveryStatus.Failed, reclaimed.DeliveryStatus);
        Assert.Equal(2, reclaimed.RetryCount);                    // MarkFailed incremented it
        Assert.NotNull(reclaimed.NextRetryAt);                    // ...and scheduled a backoff
        Assert.Contains("reclaimed", reclaimed.ErrorMessage);
        Assert.True(reclaimed.RetrySweepWillTakeIt, "reclaimed with budget left — the sweep owns it now");
        Assert.True(reclaimed.PageIsOnItsWay, "...so a page is on its way for it");

        var untouched = await ReloadAsync(cs, midSend);
        Assert.Equal(NotificationDeliveryStatus.Sending, untouched.DeliveryStatus);
        Assert.Equal(0, untouched.RetryCount);
    }

    /// <summary>A row stranded on its last attempt spends its budget and lands terminal rather than looping.</summary>
    [PostgresFact]
    public async Task AStrandedRowOnItsLastAttempt_IsDrivenTerminal_NotLoopedForever()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var lastAttempt = await SeedAsync(
            cs, NotificationDeliveryStatus.Retrying, retryCount: Notification.MaxRetries - 1, attemptedAgo: Stranded);

        await Reaper(cs).ReclaimStuckAsync(CancellationToken.None);

        var terminal = await ReloadAsync(cs, lastAttempt);
        Assert.Equal(NotificationDeliveryStatus.PermanentlyFailed, terminal.DeliveryStatus);
        Assert.Equal(Notification.MaxRetries, terminal.RetryCount);
        Assert.Null(terminal.NextRetryAt);
        Assert.False(terminal.RetrySweepWillTakeIt, "budget spent — nothing claims it again");
        Assert.False(terminal.PageIsOnItsWay, "...and nothing may report a page as still coming for it");
    }

    /// <summary>A stranded row at the attempt bound is moved by nobody, so liveness must not call it in flight.</summary>
    // Lowering MaxRetries is all it takes to make this every stranded row between the old bound and the new.
    [PostgresFact]
    public async Task AStrandedRowAlreadyAtTheBound_IsReclaimedByNobody_AndReadsAsNoPageInFlight()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var pastBound = await SeedAsync(
            cs, NotificationDeliveryStatus.Retrying, retryCount: Notification.MaxRetries, attemptedAgo: Stranded);

        await Reaper(cs).ReclaimStuckAsync(CancellationToken.None);

        var stored = await ReloadAsync(cs, pastBound);
        Assert.Equal(NotificationDeliveryStatus.Retrying, stored.DeliveryStatus);   // reaper declined it
        Assert.Equal(Notification.MaxRetries, stored.RetryCount);                   // untouched
        Assert.False(
            stored.PageIsOnItsWay,
            "a Sending/Retrying row with no budget is one the reaper will not reclaim and the sweep will "
            + "not claim; PageIsOnItsWay must not call it a page in flight, or the escalation waits on a "
            + "call nobody will place while reporting nobody as unreached.");
    }

    /// <summary>The reaper runs on both hosts, so concurrent sweeps must reclaim a row exactly once.</summary>
    // The first commit moves the row out of the window; the loser either fails its write or never selected it.
    [PostgresFact]
    public async Task TwoHostsReapingTheSameRowAtOnce_ReclaimItExactlyOnce()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var stranded = await SeedAsync(cs, NotificationDeliveryStatus.Retrying, retryCount: 1, attemptedAgo: Stranded);

        // Two hosts, each with its own scope factory and its own connections, sweeping at the same time.
        await Task.WhenAll(
            Reaper(cs).ReclaimStuckAsync(CancellationToken.None),
            Reaper(cs).ReclaimStuckAsync(CancellationToken.None));

        var afterConcurrent = await ReloadAsync(cs, stranded);
        Assert.Equal(NotificationDeliveryStatus.Failed, afterConcurrent.DeliveryStatus);
        Assert.Equal(2, afterConcurrent.RetryCount);   // reclaimed once, not twice — never 3

        // And a later sweep, on either host, leaves the already-reclaimed row alone (it is Failed now,
        // outside the Sending/Retrying set the reaper looks at).
        await Reaper(cs).ReclaimStuckAsync(CancellationToken.None);

        var afterThird = await ReloadAsync(cs, stranded);
        Assert.Equal(NotificationDeliveryStatus.Failed, afterThird.DeliveryStatus);
        Assert.Equal(2, afterThird.RetryCount);
    }

    // ---- harness ------------------------------------------------------------

    private static NotificationReaperBackgroundService Reaper(string connectionString) =>
        new(new PostgresScopeFactory(connectionString),
            NullLogger<NotificationReaperBackgroundService>.Instance);

    private static async Task<Guid> SeedAsync(
        string connectionString,
        NotificationDeliveryStatus status,
        int retryCount,
        TimeSpan attemptedAgo)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = "responder-1",
            Type = NotificationType.VoiceCall,   // a channel that CAN page, so the liveness reads are not vacuous
            Title = "Escalation step 1",
            Message = "Checkout failing",
            DeliveryStatus = status,
            RetryCount = retryCount,
            LastAttemptAt = DateTime.UtcNow - attemptedAgo,
            CreatedAt = DateTime.UtcNow.AddMinutes(-20)
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        return notification.Id;
    }

    private static async Task<Notification> ReloadAsync(string connectionString, Guid id)
    {
        await using var db = PostgresFixture.Context(connectionString);
        return await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id);
    }

    /// <summary>Hands the reaper a production-wired context per sweep without standing up a DI container.</summary>
    private sealed class PostgresScopeFactory(string connectionString) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(PostgresFixture.Context(connectionString));

        private sealed class Scope(ApplicationDbContext context) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(ApplicationDbContext) ? context : null;

            public void Dispose() => context.Dispose();
        }
    }
}
