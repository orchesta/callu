using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The retry drain: a responder's second chance at being told a page failed.</summary>
// The claim is raw SELECT … FOR UPDATE SKIP LOCKED against xmin, which only a real database has.
[Collection(PostgresCollection.Name)]
public class NotificationRetryQueueTests(PostgresFixture pg)
{
    private const string UserId = "responder-1";
    private static readonly Instant Noon = Instant.FromUtc(2026, 7, 14, 12, 0);

    /// <summary>The claim query's attempt ceiling, read from the entity so the two cannot drift.</summary>
    private const int MaxRetryCount = Notification.MaxRetries;

    /// <summary>The page that failed to go out, and is due for another go.</summary>
    [PostgresFact]
    public async Task ADueRow_IsReSentThroughItsChannel_AndEndsDelivered()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);
        var row = await SeedRowAsync(cs, incidentId, NotificationType.Email, dueAgo: TimeSpan.FromMinutes(1));

        var email = new RecordingChannelDispatcher(NotificationType.Email);
        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, [email]).ProcessNotificationQueueAsync();

        Assert.Equal([row], email.Retried.Select(n => n.Id));

        var stored = await ReloadAsync(cs, row);
        Assert.Equal(NotificationDeliveryStatus.Delivered, stored.DeliveryStatus);
        Assert.Null(stored.NextRetryAt);
    }

    /// <summary>One row's channel throwing must not cost the rest of the batch its retry.</summary>
    [PostgresFact]
    public async Task OneRowWhoseChannelThrows_DoesNotCostTheRestOfTheBatchItsRetry()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);
        var first = await SeedRowAsync(cs, incidentId, NotificationType.Email, dueAgo: TimeSpan.FromMinutes(2));
        var second = await SeedRowAsync(cs, incidentId, NotificationType.Email, dueAgo: TimeSpan.FromMinutes(1), userId: "responder-2");

        var email = new RecordingChannelDispatcher(NotificationType.Email);
        var thrown = 0;

        // Throws for the first row of the batch only — a transient provider blow-up, not a config error.
        var throwingEmail = new ThrowingOnceChannelDispatcher(email, () => thrown++ == 0);

        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, [throwingEmail]).ProcessNotificationQueueAsync();

        // Both were attempted. Before the fix to the sibling sweeps, the exception ended the batch.
        Assert.Equal(2, throwingEmail.Attempts);

        var failed = await ReloadAsync(cs, first);
        var delivered = await ReloadAsync(cs, second);

        Assert.Equal(NotificationDeliveryStatus.Failed, failed.DeliveryStatus);
        Assert.Contains("Retry:", failed.ErrorMessage);
        Assert.Equal(NotificationDeliveryStatus.Delivered, delivered.DeliveryStatus);
    }

    /// <summary>
    /// A row for a channel nobody wired up (voice configured, then the provider removed) can never
    /// make progress. It is failed with a reason rather than re-claimed on every tick forever.
    /// </summary>
    [PostgresFact]
    public async Task ARowForAChannelWithNoDispatcher_IsFailedWithAReason_NotRetriedForever()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);
        var row = await SeedRowAsync(cs, incidentId, NotificationType.VoiceCall, dueAgo: TimeSpan.FromMinutes(1));

        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, [new RecordingChannelDispatcher(NotificationType.Email)]).ProcessNotificationQueueAsync();

        var stored = await ReloadAsync(cs, row);
        Assert.Contains("Unsupported channel", stored.ErrorMessage);
        Assert.NotEqual(NotificationDeliveryStatus.Delivered, stored.DeliveryStatus);
    }

    /// <summary>A page whose backoff has not elapsed is not due. Sending it early rings a phone twice.</summary>
    [PostgresFact]
    public async Task ARowStillInItsBackoff_IsLeftAlone()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);
        var row = await SeedRowAsync(cs, incidentId, NotificationType.Email, dueAgo: TimeSpan.FromMinutes(-10));

        var email = new RecordingChannelDispatcher(NotificationType.Email);
        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, [email]).ProcessNotificationQueueAsync();

        Assert.Empty(email.Retried);
        Assert.Equal(NotificationDeliveryStatus.Failed, (await ReloadAsync(cs, row)).DeliveryStatus);
    }

    /// <summary>The chain is bounded: a row that has spent its attempts is not claimed again.</summary>
    [PostgresFact]
    public async Task ARowAtTheAttemptLimit_IsNotClaimed()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);
        await SeedRowAsync(cs, incidentId, NotificationType.Email, dueAgo: TimeSpan.FromMinutes(1), retryCount: MaxRetryCount);

        var email = new RecordingChannelDispatcher(NotificationType.Email);
        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, [email]).ProcessNotificationQueueAsync();

        Assert.Empty(email.Retried);
    }

    /// <summary>A soft-deleted row is never claimed, however due everything else about it looks.</summary>
    // It does not prove the claim SQL's own IsDeleted predicate: the context's query filter excludes it too.
    [PostgresFact]
    public async Task ASoftDeletedRow_IsNotClaimed_HoweverDueItLooks()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);
        var row = await SeedRowAsync(
            cs, incidentId, NotificationType.Email, dueAgo: TimeSpan.FromHours(1), isDeleted: true);

        var email = new RecordingChannelDispatcher(NotificationType.Email);
        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, [email]).ProcessNotificationQueueAsync();

        Assert.Empty(email.Retried);

        // Untouched — not even claimed into Sending. Nothing is coming for this row, and the entity that
        // the escalation's "reached" count is built on has to agree.
        var stored = await ReloadIgnoringFiltersAsync(cs, row);
        Assert.Equal(NotificationDeliveryStatus.Failed, stored.DeliveryStatus);
        Assert.False(stored.RetrySweepWillTakeIt, "the sweep just declined to take it");
        Assert.False(stored.PageIsOnItsWay);
        Assert.False(stored.CountsAsReached);
    }

    /// <summary>A page that was delivered in the meantime is not sent a second time.</summary>
    [PostgresFact]
    public async Task ADeliveredRow_IsNotReSent()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);
        await SeedRowAsync(
            cs, incidentId, NotificationType.Email, dueAgo: TimeSpan.FromMinutes(1),
            status: NotificationDeliveryStatus.Delivered);

        var email = new RecordingChannelDispatcher(NotificationType.Email);
        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, [email]).ProcessNotificationQueueAsync();

        Assert.Empty(email.Retried);
    }

    // ---- harness ------------------------------------------------------------

    /// <summary>Wraps a channel and throws for the rows a predicate picks, counting every attempt.</summary>
    private sealed class ThrowingOnceChannelDispatcher(
        RecordingChannelDispatcher inner, Func<bool> shouldThrow) : INotificationChannelDispatcher
    {
        public int Attempts { get; private set; }

        public NotificationType Channel => inner.Channel;

        public Task SendAsync(
            Notification notification, string? email, string? phoneNumber,
            Callu.Shared.Models.Notifications.NotificationPayload payload, string? incidentUrl,
            CancellationToken cancellationToken = default) =>
            inner.SendAsync(notification, email, phoneNumber, payload, incidentUrl, cancellationToken);

        public Task RetryAsync(Notification notification, string? baseUrl, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (shouldThrow())
                throw new InvalidOperationException("the mail server closed the connection");

            return inner.RetryAsync(notification, baseUrl, cancellationToken);
        }

        public Task<(bool Success, string Message)> SendTestAsync(
            string userId, string? email, string? phoneNumber, CancellationToken cancellationToken = default) =>
            inner.SendTestAsync(userId, email, phoneNumber, cancellationToken);
    }

    private static async Task<Guid> SeedIncidentAsync(string connectionString)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var incident = new Incident
        {
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.Open,
            StartedAt = DateTime.UtcNow
        };

        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        return incident.Id;
    }

    // dueAgo is how long ago the row became due; negative means it is still inside its backoff.
    private static async Task<Guid> SeedRowAsync(
        string connectionString,
        Guid incidentId,
        NotificationType type,
        TimeSpan dueAgo,
        string userId = UserId,
        int retryCount = 1,
        NotificationDeliveryStatus status = NotificationDeliveryStatus.Failed,
        bool isDeleted = false)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IncidentId = incidentId,
            Type = type,
            Title = "Escalation step 1",
            Message = "Checkout failing",
            DeliveryStatus = status,
            RetryCount = retryCount,
            NextRetryAt = DateTime.UtcNow - dueAgo,
            ErrorMessage = "smtp: connection refused",
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
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

    /// <summary>The soft-delete query filter hides a deleted row from an ordinary read; the point here is to look at it anyway.</summary>
    private static async Task<Notification> ReloadIgnoringFiltersAsync(string connectionString, Guid id)
    {
        await using var db = PostgresFixture.Context(connectionString);
        return await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.Id == id);
    }

    private static NotificationDispatcher Dispatcher(
        ApplicationDbContext context, IEnumerable<INotificationChannelDispatcher> channels)
    {
        var contacts = Substitute.For<IUserContactRepository>();
        contacts.GetContactByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new UserContactSnapshot(
                call.Arg<string>(), "Responder", "+905551112233", "responder@example.io"));

        var settings = Substitute.For<IOrganizationSettingsService>();
        settings.GetPublicBaseUrlAsync(Arg.Any<CancellationToken>()).Returns("https://callu.example.io");

        return new NotificationDispatcher(
            new NotificationRepository(context, NullLogger<NotificationRepository>.Instance),
            new NotificationPreferenceRepository(context, NullLogger<NotificationPreferenceRepository>.Instance),
            new TeamMemberRepository(context, NullLogger<TeamMemberRepository>.Instance),
            new TransactionManager(context, NullLogger<TransactionManager>.Instance),
            contacts,
            Substitute.For<IOnCallService>(),
            settings,
            DateTimeZoneProviders.Tzdb,
            new FixedClock(Noon),
            channels,
            context,
            NullLogger<NotificationDispatcher>.Instance);
    }
}
