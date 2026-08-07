using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// The retry sweep claims up to fifty rows and sends them one at a time, every ten seconds, one
/// worker at a time. A voice provider that takes a minute a row used to make that pass an hour long,
/// and every e-mail and SMS retry in the same batch waited behind it.
/// </summary>
// The claim is raw SELECT … FOR UPDATE SKIP LOCKED against xmin, which only a real database has.
[Collection(PostgresCollection.Name)]
public class NotificationSweepBudgetTests(PostgresFixture pg)
{
    private static readonly Instant Start = Instant.FromUtc(2026, 7, 30, 3, 0);

    private sealed class AdvancingClock(Instant now) : IClock
    {
        private Instant _now = now;

        public Instant GetCurrentInstant() => _now;

        public void Advance(TimeSpan by) => _now += Duration.FromTimeSpan(by);
    }

    private sealed class SlowChannelDispatcher(
        NotificationType channel, AdvancingClock clock, TimeSpan perCall) : INotificationChannelDispatcher
    {
        public NotificationType Channel => channel;

        public List<Guid> Retried { get; } = [];

        public Task SendAsync(
            Notification notification, string? email, string? phoneNumber,
            NotificationPayload payload, string? incidentUrl, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RetryAsync(Notification notification, string? baseUrl, CancellationToken cancellationToken = default)
        {
            Retried.Add(notification.Id);
            clock.Advance(perCall);
            notification.MarkDelivered();
            return Task.CompletedTask;
        }

        public Task<(bool Success, string Message)> SendTestAsync(
            string userId, string? email, string? phoneNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, "ok"));
    }

    /// <summary>
    /// Six due voice rows behind a provider that takes forty-five seconds each: two go out, the rest
    /// keep their place in the queue, and the pass ends instead of running for four and a half minutes.
    /// </summary>
    [PostgresFact]
    public async Task AWedgedChannelGetsABoundedShareOfOneSweep()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);
        for (var i = 0; i < 6; i++)
            await SeedDueRowAsync(cs, incidentId, NotificationType.VoiceCall, $"responder-{i}");

        var clock = new AdvancingClock(Start);
        var voice = new SlowChannelDispatcher(NotificationType.VoiceCall, clock, TimeSpan.FromSeconds(45));

        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, clock, [voice]).ProcessNotificationQueueAsync();

        Assert.Equal(2, voice.Retried.Count);
        Assert.True(clock.GetCurrentInstant() - Start <= Duration.FromSeconds(105));
    }

    /// <summary>The rows that did not fit are still due, with their retry budget untouched.</summary>
    [PostgresFact]
    public async Task TheRowsThatDidNotFitStayDue_WithTheirBudgetIntact()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);
        var seeded = new List<Guid>();
        for (var i = 0; i < 6; i++)
            seeded.Add(await SeedDueRowAsync(cs, incidentId, NotificationType.VoiceCall, $"responder-{i}", retryCount: 1));

        var clock = new AdvancingClock(Start);
        var voice = new SlowChannelDispatcher(NotificationType.VoiceCall, clock, TimeSpan.FromSeconds(45));

        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, clock, [voice]).ProcessNotificationQueueAsync();

        await using var verify = PostgresFixture.Context(cs);
        var untouched = await verify.Notifications.AsNoTracking()
            .Where(n => seeded.Contains(n.Id) && !voice.Retried.Contains(n.Id))
            .ToListAsync();

        Assert.Equal(4, untouched.Count);
        Assert.All(untouched, row =>
        {
            Assert.Equal(1, row.RetryCount);
            Assert.True(row.RetrySweepWillTakeIt, "a row the sweep ran out of time for must still be claimable");
            Assert.True(row.PageIsOnItsWay);
        });
    }

    /// <summary>
    /// The failure this bound exists for: e-mail and SMS retries queued behind the wedged channel go
    /// out on the same pass instead of waiting for it to drain.
    /// </summary>
    [PostgresFact]
    public async Task TheOtherChannelsInTheSameBatchAreNotHeldBehindIt()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var incidentId = await SeedIncidentAsync(cs);

        // Voice first in CreatedAt order, so an unbounded sweep reaches the e-mail rows last.
        for (var i = 0; i < 6; i++)
            await SeedDueRowAsync(cs, incidentId, NotificationType.VoiceCall, $"voice-{i}", createdMinutesAgo: 30 - i);
        for (var i = 0; i < 3; i++)
            await SeedDueRowAsync(cs, incidentId, NotificationType.Email, $"mail-{i}", createdMinutesAgo: 10 - i);

        var clock = new AdvancingClock(Start);
        var voice = new SlowChannelDispatcher(NotificationType.VoiceCall, clock, TimeSpan.FromSeconds(45));
        var email = new SlowChannelDispatcher(NotificationType.Email, clock, TimeSpan.Zero);

        await using (var db = PostgresFixture.Context(cs))
            await Dispatcher(db, clock, [voice, email]).ProcessNotificationQueueAsync();

        Assert.Equal(3, email.Retried.Count);
        Assert.Equal(2, voice.Retried.Count);
    }

    // ---- harness ------------------------------------------------------------

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

    private static async Task<Guid> SeedDueRowAsync(
        string connectionString,
        Guid incidentId,
        NotificationType type,
        string userId,
        int retryCount = 1,
        int createdMinutesAgo = 5)
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
            DeliveryStatus = NotificationDeliveryStatus.Failed,
            RetryCount = retryCount,
            NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
            ErrorMessage = "the provider did not answer",
            CreatedAt = DateTime.UtcNow.AddMinutes(-createdMinutesAgo)
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        return notification.Id;
    }

    private static NotificationDispatcher Dispatcher(
        ApplicationDbContext context, IClock clock, IEnumerable<INotificationChannelDispatcher> channels)
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
            clock,
            channels,
            context,
            NullLogger<NotificationDispatcher>.Instance);
    }
}
