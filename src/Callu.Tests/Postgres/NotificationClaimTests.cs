using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The notification claim's three outcomes — Claimed, AlreadyPaged, Failed — against real Postgres.</summary>
// The unique index that raises 23505 exists only here, and the orchestrator acts on all three differently.
[Collection(PostgresCollection.Name)]
public class NotificationClaimTests(PostgresFixture pg)
{
    private const string UserId = "responder-1";
    private static readonly Instant Noon = Instant.FromUtc(2026, 7, 13, 12, 0);

    // ---- AlreadyPaged: the real dedupe race ---------------------------------

    /// <summary>The loser of a dedupe race reads 23505 as "already being paged", not as a failure.</summary>
    [PostgresFact]
    public async Task LosingTheDedupeRace_CountsTheUserAsReached_AndSendsNothingTwice()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var incidentId = await SeedIncidentAsync(cs);

        var payload = Payload(incidentId, generation: 100);
        var email = new RecordingChannelDispatcher(NotificationType.Email);

        // The rival dispatch commits its row inside the window between our dedupe check and our
        // INSERT — exactly the race a second Worker replica runs.
        await using var racing = new RacingDbContext(
            Options(cs), () => CommitRivalClaimAsync(cs, payload));

        var result = await Dispatcher(racing, [email]).NotifyUsersAsync([UserId], payload);

        Assert.Equal(new NotificationDispatchResult(1, 0), result);
        Assert.False(result.DispatchFailed, "losing the dedupe race is not a dispatch failure");
        Assert.Empty(email.Sent);

        await using var db = PostgresFixture.Context(cs);
        Assert.Equal(1, await db.Notifications.CountAsync());
    }

    /// <summary>
    /// The same thing without rigging anything: several dispatchers page the same step at once and
    /// the database is left holding exactly one row. Nobody is called twice.
    /// </summary>
    [PostgresFact]
    public async Task ConcurrentDispatchesOfOneStep_LeaveExactlyOneNotification()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var incidentId = await SeedIncidentAsync(cs);

        var payload = Payload(incidentId, generation: 200);
        var sends = 0;

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = PostgresFixture.Context(cs);
            var email = new RecordingChannelDispatcher(NotificationType.Email);

            var result = await Dispatcher(db, [email]).NotifyUsersAsync([UserId], payload);
            Interlocked.Add(ref sends, email.Sent.Count);
            return result;
        }));

        await using var verify = PostgresFixture.Context(cs);
        Assert.Equal(1, await verify.Notifications.CountAsync());
        Assert.Equal(1, sends);

        // Every racer reports the user as reached: the page exists, so the orchestrator must not
        // hear "nobody was paged" from the seven that lost.
        Assert.All(results, r =>
        {
            Assert.Equal(1, r.Reached);
            Assert.False(r.DispatchFailed);
            Assert.False(r.NobodyToPage);
        });
    }

    /// <summary>The winner's row is a real, sendable page — the race must not leave a husk behind.</summary>
    [PostgresFact]
    public async Task TheWinnerOfTheRace_ActuallySends()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var incidentId = await SeedIncidentAsync(cs);

        var payload = Payload(incidentId, generation: 300);
        var email = new RecordingChannelDispatcher(NotificationType.Email);

        await using var db = PostgresFixture.Context(cs);
        await Dispatcher(db, [email]).NotifyUsersAsync([UserId], payload);

        Assert.Single(email.Sent);

        await using var verify = PostgresFixture.Context(cs);
        var row = await verify.Notifications.SingleAsync();
        Assert.Equal(NotificationDeliveryStatus.Delivered, row.DeliveryStatus);
        Assert.Equal(
            NotificationFactory.ComputeDedupeKey(UserId, payload, NotificationType.Email, payload.DispatchGeneration),
            row.DedupeKey);
    }

    // ---- Failed: a store error is not an empty rota -------------------------

    /// <summary>A non-dedupe store error comes back as Failed, which re-runs the step, not as an empty rota.</summary>
    [PostgresFact]
    public async Task AStoreErrorOnClaim_ComesBackAsFailed_NotAsNobodyToPage()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await SeedIncidentAsync(cs);

        var payload = Payload(Guid.NewGuid(), generation: 400);
        var email = new RecordingChannelDispatcher(NotificationType.Email);

        await using var db = PostgresFixture.Context(cs);
        var result = await Dispatcher(db, [email]).NotifyUsersAsync([UserId], payload);

        Assert.Equal(0, result.Reached);
        Assert.Equal(1, result.Failed);
        Assert.True(result.DispatchFailed);
        Assert.False(result.NobodyToPage, "a store error must never be reported as an empty rota");
        Assert.Equal(NotificationType.Email, Assert.Single(result.ChannelFailures!).Channel);
        Assert.Empty(email.Sent);

        await using var verify = PostgresFixture.Context(cs);
        Assert.Equal(0, await verify.Notifications.CountAsync());
    }

    /// <summary>A failed claim on one channel leaves the others sending, but travels back in ChannelFailures.</summary>
    // "Reached" only means some channel landed, so a lost one has to reach an operator rather than vanish.
    [PostgresFact]
    public async Task OneChannelFailingToClaim_DoesNotStopTheOthers_ButIsStillReported()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var incidentId = await SeedIncidentAsync(cs);
        await SeedPreferenceAsync(cs, sms: true);

        var payload = Payload(incidentId, generation: 500);
        var email = new RecordingChannelDispatcher(NotificationType.Email);
        var sms = new RecordingChannelDispatcher(NotificationType.Sms);

        // The SMS row is over the Title column's 200-char limit, so its INSERT is rejected (22001)
        // while the email row — built from the same payload but a shorter title — is not.
        await using var db = new OversizedTitleDbContext(Options(cs), NotificationType.Sms);
        var result = await Dispatcher(db, [email, sms]).NotifyUsersAsync([UserId], payload);

        Assert.Equal(1, result.Reached);
        Assert.False(result.DispatchFailed);
        Assert.Single(email.Sent);
        Assert.Empty(sms.Sent);

        // The SMS page was never queued and nothing will retry it — reported, not swallowed.
        Assert.True(result.PartiallyFailed);
        var lost = Assert.Single(result.ChannelFailures!);
        Assert.Equal(NotificationType.Sms, lost.Channel);
        Assert.Equal(UserId, lost.UserId);

        await using var verify = PostgresFixture.Context(cs);
        var row = await verify.Notifications.SingleAsync();
        Assert.Equal(NotificationType.Email, row.Type);
    }

    // ---- harness ------------------------------------------------------------

    private static DbContextOptions<ApplicationDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsql.UseNodaTime();
            })
            .Options;

    /// <summary>Commits the rival dispatch's row on its own connection, once, just before ours goes in.</summary>
    private sealed class RacingDbContext(DbContextOptions<ApplicationDbContext> options, Func<Task> rival)
        : ApplicationDbContext(options)
    {
        private bool _fired;

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (!_fired)
            {
                _fired = true;
                await rival();
            }

            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    /// <summary>Blows past the Title column on one channel only, so the other channel's claim still lands.</summary>
    private sealed class OversizedTitleDbContext(DbContextOptions<ApplicationDbContext> options, NotificationType channel)
        : ApplicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries<Notification>())
                if (entry.State == EntityState.Added && entry.Entity.Type == channel)
                    entry.Entity.Title = new string('x', 400);

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    /// <summary>The rival dispatch's row, written the way a dispatch actually writes one.</summary>
    // MarkSending precedes the claim in production, so the row a racer loses to must be Sending, not Pending.
    private static async Task CommitRivalClaimAsync(string connectionString, NotificationPayload payload)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var rival = NotificationFactory.Create(
            UserId, payload, "https://callu.example.io/incidents/x",
            NotificationType.Email, payload.DispatchGeneration);
        rival.MarkSending();

        db.Notifications.Add(rival);
        await db.SaveChangesAsync();
    }

    private static NotificationPayload Payload(Guid incidentId, long generation) => new()
    {
        IncidentId = incidentId,
        Title = "Checkout failing",
        Severity = "Critical",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1,
        DispatchGeneration = generation
    };

    private static NotificationDispatcher Dispatcher(
        ApplicationDbContext context, IEnumerable<INotificationChannelDispatcher> channels)
    {
        var contacts = Substitute.For<IUserContactRepository>();
        contacts.GetContactByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(UserId, "Responder One", "+905551112233", "responder1@example.io"));

        var settings = Substitute.For<IOrganizationSettingsService>();
        settings.GetPublicBaseUrlAsync(Arg.Any<CancellationToken>()).Returns("https://callu.example.io");

        return new NotificationDispatcher(
            new NotificationRepository(context, NullLogger<NotificationRepository>.Instance),
            new NotificationPreferenceRepository(context, NullLogger<NotificationPreferenceRepository>.Instance),
            new TeamMemberRepository(context, NullLogger<TeamMemberRepository>.Instance),
            new ImmediateTransactionManager(),
            contacts,
            Substitute.For<IOnCallService>(),
            settings,
            DateTimeZoneProviders.Tzdb,
            new FixedClock(Noon),
            channels,
            context,
            NullLogger<NotificationDispatcher>.Instance);
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

    private static async Task SeedPreferenceAsync(string connectionString, bool sms)
    {
        await using var db = PostgresFixture.Context(connectionString);

        db.NotificationPreferences.Add(new NotificationPreference
        {
            UserId = UserId,
            EmailEnabled = true,
            SmsEnabled = sms,
            VoiceEnabled = false,
            PushEnabled = false,
            Timezone = "UTC"
        });

        await db.SaveChangesAsync();
    }
}
