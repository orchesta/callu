using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Callu.Tests;

/// <summary>Every reason written to <c>Notification.ErrorMessage</c> is clamped to the column it lands in.</summary>
// EF InMemory ignores HasMaxLength, so only a real column can prove the clamp is load-bearing.
[Collection(PostgresCollection.Name)]
public class NotificationErrorMessageOverflowTests(PostgresFixture pg)
{
    /// <summary>A StackExchange.Redis timeout message, reproduced in shape and length.</summary>
    private static string RedisTimeoutMessage()
    {
        var message =
            "Timeout performing GET (5000ms), next: GET callu:notif:user-1, inst: 0, qu: 0, qs: 12, aw: False, "
            + "bw: SpinningDown, rs: ReadAsync, ws: Idle, in: 0, in-pipe: 0, out-pipe: 0, last-in: 0, cur-in: 0, "
            + "sync-ops: 0, async-ops: 4412, serverEndpoint: redis:6379, conn-sec: 3821.44, aoc: 0, mc: 1/1/0, "
            + "mgr: 10 of 10 available, clientName: callu-worker-7d9f8c4b6d-x2klm(SE.Redis-v2.8.16), "
            + "IOCP: (Busy=0,Free=1000,Min=8,Max=1000), WORKER: (Busy=12,Free=32755,Min=8,Max=32767), "
            + "POOL: (Threads=16,QueuedItems=3,CompletedItems=98213,Timers=41), v: 2.8.16.12844";

        // The bug is length, so the test must be honest about it: this really is over the column's bound.
        Assert.True(message.Length > Notification.MaxErrorMessageLength,
            $"this fixture is supposed to OVERFLOW varchar({Notification.MaxErrorMessageLength}); it is only "
            + $"{message.Length} characters, so it would prove nothing");

        return message;
    }

    /// <summary>
    /// The one that used to throw 22001. A Redis timeout during the push, written to the row, committed.
    /// </summary>
    [PostgresFact]
    public async Task ARedisTimeoutMessage_OnASkippedPush_DoesNotBlowUpSaveChanges()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var notification = Page(NotificationType.Push);
        notification.MarkSkipped($"Real-time push unavailable: {RedisTimeoutMessage()}");

        await using var db = PostgresFixture.Context(cs);
        db.Add(notification);

        // Before the clamp this threw PostgresException 22001, and the row never recorded the real fault.
        await db.SaveChangesAsync();

        var stored = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationDeliveryStatus.Skipped, stored.DeliveryStatus);
        Assert.NotNull(stored.ErrorMessage);
        Assert.True(stored.ErrorMessage!.Length <= Notification.MaxErrorMessageLength);

        // Truncated, not discarded: what an operator reads still names the fault.
        Assert.StartsWith("Real-time push unavailable: Timeout performing GET", stored.ErrorMessage);
    }

    /// <summary>Every setter that writes the reason clamps, not just the push path.</summary>
    // MarkFailed is on the retry path, where a throw strands the row as well as losing the reason.
    [PostgresFact]
    public async Task EverySetterThatWritesTheReason_SurvivesAnUnboundedProviderError()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var huge = new string('x', 4000);

        var failed = Page(NotificationType.Email);
        failed.MarkFailed($"Channel dispatch error: {huge}");

        var permanent = Page(NotificationType.Sms);
        permanent.MarkPermanentlyFailed($"sms retry attempt 3: {huge}");

        var deferred = Page(NotificationType.VoiceCall);
        deferred.MarkDeferred(DateTime.UtcNow.AddSeconds(60), $"no voice provider is registered: {huge}");

        await using var db = PostgresFixture.Context(cs);
        db.AddRange(failed, permanent, deferred);

        await db.SaveChangesAsync();

        foreach (var stored in await db.Notifications.AsNoTracking().ToListAsync())
            Assert.True(
                stored.ErrorMessage!.Length <= Notification.MaxErrorMessageLength,
                $"{stored.Type} stored a {stored.ErrorMessage.Length}-character reason in a "
                + $"varchar({Notification.MaxErrorMessageLength}) column");
    }

    /// <summary>Clamping on a surrogate boundary would trade the length error for an encoding error.</summary>
    [PostgresFact]
    public async Task ClampingNeverSplitsASurrogatePair_SoTheRowStillEncodes()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        // "🔥" is one surrogate PAIR (two chars), so a run of them straddles the 500-char boundary at the
        // worst possible offset: char 499 is a high surrogate whose partner is char 500.
        var notification = Page(NotificationType.Email);
        notification.MarkFailed(new string('a', 1) + string.Concat(Enumerable.Repeat("🔥", 400)));

        await using var db = PostgresFixture.Context(cs);
        db.Add(notification);

        await db.SaveChangesAsync();

        var stored = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.True(stored.ErrorMessage!.Length <= Notification.MaxErrorMessageLength);
        Assert.False(char.IsHighSurrogate(stored.ErrorMessage[^1]),
            "the reason ends on a lone high surrogate — that is not valid UTF-8 and Postgres will refuse it");
    }

    /// <summary>The column really is the bound: written past the setters, an oversized reason is rejected.</summary>
    // Without this, a widened column would leave the clamp tests passing on a constraint that no longer exists.
    [PostgresFact]
    public async Task TheColumnStillRejectsAnOversizedReason_SoTheClampIsLoadBearing()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var notification = Page(NotificationType.Email);
        notification.MarkFailed("short enough");

        await using var db = PostgresFixture.Context(cs);
        db.Add(notification);
        await db.SaveChangesAsync();

        // Straight at the column, bypassing Notification.Clamp entirely.
        var oversized = new string('x', Notification.MaxErrorMessageLength + 1);

        var thrown = await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Notifications" SET "ErrorMessage" = {oversized} WHERE "Id" = {notification.Id}"""));

        Assert.Equal("22001", thrown.SqlState);
    }

    private static Notification Page(NotificationType channel) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "responder-1",
        Type = channel,
        Title = "Database unreachable",
        Message = "Severity: High",
        DedupeKey = $"{Guid.NewGuid():N}",
        CreatedAt = DateTime.UtcNow
    };
}
