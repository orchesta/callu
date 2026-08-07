using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Notifications;
using Callu.Shared.Models.Schedules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Covers the paging funnel end-to-end over the real notification repository, with a fixed clock.</summary>
public class NotificationDispatcherTests : IDisposable
{
    private const string UserId = "user-1";
    private static readonly Guid IncidentId = Guid.NewGuid();
    private static readonly Instant Noon = Instant.FromUtc(2026, 6, 15, 12, 0);

    private readonly string _dbName = $"dispatch-{Guid.NewGuid():N}";
    private readonly ApplicationDbContext _ctx;
    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();
    private readonly INotificationPushService _push = Substitute.For<INotificationPushService>();

    public NotificationDispatcherTests()
    {
        _ctx = new ApplicationDbContext(Options(_dbName));

        WithContact(phone: "+905551112233");
    }

    private static DbContextOptions<ApplicationDbContext> Options(string dbName) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options;

    public void Dispose() => _ctx.Dispose();

    private void WithContact(string? phone)
        => _contacts.GetContactByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(UserId, "Test User", phone, "user1@example.io"));

    private NotificationDispatcher Sut(
        Instant? now = null,
        IEnumerable<INotificationChannelDispatcher>? channels = null,
        IVoiceCallCoalescingGuard? coalescingGuard = null,
        INotificationPushService? push = null,
        ApplicationDbContext? ctx = null,
        IOnCallService? onCall = null)
    {
        var settings = Substitute.For<IOrganizationSettingsService>();
        settings.GetPublicBaseUrlAsync(Arg.Any<CancellationToken>()).Returns("https://callu.example.io");

        var context = ctx ?? _ctx;

        return new NotificationDispatcher(
            new NotificationRepository(context, NullLogger<NotificationRepository>.Instance),
            new NotificationPreferenceRepository(context, NullLogger<NotificationPreferenceRepository>.Instance),
            new TeamMemberRepository(context, NullLogger<TeamMemberRepository>.Instance),
            new ImmediateTransactionManager(),
            _contacts,
            onCall ?? Substitute.For<IOnCallService>(),
            settings,
            DateTimeZoneProviders.Tzdb,
            new FixedClock(now ?? Noon),
            channels ?? [new RecordingChannelDispatcher(NotificationType.Email)],
            context,
            NullLogger<NotificationDispatcher>.Instance,
            push,
            coalescingGuard);
    }

    /// <summary>
    /// Stands in for the notification store being briefly unavailable while a step is being paged:
    /// the row is tracked, the write fails, nothing is queued and nothing will retry itself.
    /// </summary>
    private sealed class FailingDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
            => Task.FromException<int>(new DbUpdateException("notification store is unavailable"));
    }

    /// <summary>
    /// Stands in for the store rejecting ONE channel's row (an oversized column, a constraint) while
    /// the user's other channels are claimed normally — the partial loss that used to be swallowed.
    /// </summary>
    private sealed class ChannelFailingDbContext(
        DbContextOptions<ApplicationDbContext> options, NotificationType rejected)
        : ApplicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
            => ChangeTracker.Entries<Notification>()
                .Any(e => e.State == EntityState.Added && e.Entity.Type == rejected)
                ? Task.FromException<int>(new DbUpdateException($"the notification store rejected the {rejected} row"))
                : base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static NotificationPayload Payload(long generation = 0, int level = 1, bool includeSecondary = false) => new()
    {
        IncidentId = IncidentId,
        Title = "Database unreachable",
        Severity = "High",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = level,
        DispatchGeneration = generation,
        IncludeSecondaryOnCall = includeSecondary
    };

    private void SeedPreference(
        bool email = true, bool sms = false, bool voice = false, bool push = true,
        string? quietStart = null, string? quietEnd = null, string timezone = "UTC")
    {
        _ctx.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            EmailEnabled = email,
            SmsEnabled = sms,
            VoiceEnabled = voice,
            PushEnabled = push,
            QuietHoursStart = quietStart,
            QuietHoursEnd = quietEnd,
            Timezone = timezone
        });
        _ctx.SaveChanges();
    }

    private Task<List<Notification>> Rows() =>
        _ctx.Notifications.AsNoTracking().ToListAsync();

    // ---- channel selection -------------------------------------------------

    [Fact]
    public async Task NoPreferenceRow_DefaultsToEmailAndPush()
    {
        var sut = Sut(push: _push);

        var reached = await sut.NotifyUsersAsync([UserId], Payload());

        Assert.Equal(new NotificationDispatchResult(1, 0), reached);
        var rows = await Rows();
        Assert.Equal(
            new[] { NotificationType.Email, NotificationType.Push },
            rows.Select(r => r.Type).OrderBy(t => t));
    }

    [Fact]
    public async Task SmsAndVoice_AreOptIn_AndNeedAPhoneNumber()
    {
        SeedPreference(sms: true, voice: true, push: false);
        var channels = new INotificationChannelDispatcher[]
        {
            new RecordingChannelDispatcher(NotificationType.Email),
            new RecordingChannelDispatcher(NotificationType.Sms),
            new RecordingChannelDispatcher(NotificationType.VoiceCall)
        };

        var reached = await Sut(channels: channels).NotifyUsersAsync([UserId], Payload());

        Assert.Equal(new NotificationDispatchResult(1, 0), reached);
        var types = (await Rows()).Select(r => r.Type).ToHashSet();
        Assert.Equal(
            new HashSet<NotificationType> { NotificationType.Email, NotificationType.Sms, NotificationType.VoiceCall },
            types);
    }

    [Fact]
    public async Task PhonelessUser_GetsNoSmsOrVoiceRow()
    {
        WithContact(phone: null);
        SeedPreference(sms: true, voice: true, push: false);
        var channels = new INotificationChannelDispatcher[]
        {
            new RecordingChannelDispatcher(NotificationType.Email),
            new RecordingChannelDispatcher(NotificationType.Sms),
            new RecordingChannelDispatcher(NotificationType.VoiceCall)
        };

        await Sut(channels: channels).NotifyUsersAsync([UserId], Payload());

        var types = (await Rows()).Select(r => r.Type).ToList();
        Assert.Equal([NotificationType.Email], types);
    }

    /// <summary>A responder with every channel switched off is unpageable, not an empty rota.</summary>
    [Fact]
    public async Task EveryChannelOff_ReportsAnUnpageableResponder_NotAnEmptyRota()
    {
        SeedPreference(email: false, push: false);

        var result = await Sut().NotifyUsersAsync([UserId], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.TargetsUnpageable);
        Assert.False(result.NobodyToPage, "the responder exists — it is their channels that do not");
        Assert.False(result.ChannelsSilent, "no channel was even asked, so nothing fell silent");
        Assert.Equal(UserId, Assert.Single(result.UnpageableTargets!).UserId);
        Assert.Empty(await Rows());
    }

    /// <summary>A user the contact lookup cannot resolve is reported as unpageable, not as an empty rota.</summary>
    [Fact]
    public async Task UserWithNoContactRecord_ReportsUnpageable_NotAnEmptyRota()
    {
        _contacts.GetContactByIdAsync("ghost", Arg.Any<CancellationToken>())
            .Returns((UserContactSnapshot?)null);

        var result = await Sut().NotifyUsersAsync(["ghost"], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.TargetsUnpageable);
        Assert.False(result.NobodyToPage, "the rota points to this user — the account, not the rota, is the gap");
        Assert.False(result.ChannelsSilent, "no channel was even asked");
        var target = Assert.Single(result.UnpageableTargets!);
        Assert.Equal("ghost", target.UserId);
        Assert.Contains("contact record", target.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await Rows());
    }

    // ---- quiet hours -------------------------------------------------------

    [Fact]
    public async Task QuietHours_DoNotSuppressAnEscalationPage()
    {
        // 21:00 UTC == 00:00 Europe/Istanbul, inside the 22:00→08:00 wrap-around window.
        SeedPreference(quietStart: "22:00", quietEnd: "08:00", timezone: "Europe/Istanbul", push: false);

        var reached = await Sut(now: Instant.FromUtc(2026, 6, 15, 21, 0))
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(new NotificationDispatchResult(1, 0), reached);
        Assert.Single(await Rows());
    }

    [Fact]
    public async Task QuietHoursWithUnknownTimezone_DoesNotSuppress()
    {
        SeedPreference(quietStart: "22:00", quietEnd: "08:00", timezone: "Mars/Olympus", push: false);

        var reached = await Sut(now: Instant.FromUtc(2026, 6, 15, 23, 0))
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(new NotificationDispatchResult(1, 0), reached);
    }

    // ---- voice-call coalescing --------------------------------------------

    [Fact]
    public async Task VoiceCooldown_DefersTheRowInsteadOfDialing()
    {
        SeedPreference(email: false, voice: true, push: false);
        var voice = new RecordingChannelDispatcher(NotificationType.VoiceCall);
        var guard = Substitute.For<IVoiceCallCoalescingGuard>();
        var until = Noon.ToDateTimeUtc().AddMinutes(5);
        guard.GetDeferralUntilAsync(UserId, IncidentId, true, Arg.Any<CancellationToken>()).Returns(until);

        var result = await Sut(channels: [voice], coalescingGuard: guard)
            .NotifyUsersAsync([UserId], Payload());

        // The page counts — the sweep owns it and the phone rings when the cooldown clears...
        Assert.Equal(1, result.Reached);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Silent);
        Assert.Empty(voice.Sent);

        // ...and it is reported as QUEUED, not as rung. The step advances and waits for it, but the
        // operator is told no phone has actually rung yet.
        var deferral = Assert.Single(result.Deferrals!);
        Assert.Equal(NotificationType.VoiceCall, deferral.Channel);
        Assert.Equal(until, deferral.NextAttemptAt);

        var row = Assert.Single(await Rows());
        Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
        Assert.Equal(until, row.NextRetryAt);
        Assert.Equal(0, row.RetryCount);
    }

    [Fact]
    public async Task NoCooldown_DialsImmediately()
    {
        SeedPreference(email: false, voice: true, push: false);
        var voice = new RecordingChannelDispatcher(NotificationType.VoiceCall);
        var guard = Substitute.For<IVoiceCallCoalescingGuard>();
        guard.GetDeferralUntilAsync(UserId, IncidentId, true, Arg.Any<CancellationToken>())
            .Returns((DateTime?)null);

        await Sut(channels: [voice], coalescingGuard: guard).NotifyUsersAsync([UserId], Payload());

        Assert.Single(voice.Sent);
    }

    // ---- H01: dedupe generation + honest "reached" -------------------------

    /// <summary>A redispatch inside one run sends nothing twice yet still reports the user as reached.</summary>
    [Fact]
    public async Task RepagingTheSameRun_IsDedupeSkipped_ButTheUserStillCountsAsReached()
    {
        var email = new RecordingChannelDispatcher(NotificationType.Email);
        SeedPreference(push: false);
        var sut = Sut(channels: [email]);

        var first = await sut.NotifyUsersAsync([UserId], Payload(generation: 100));
        var second = await sut.NotifyUsersAsync([UserId], Payload(generation: 100));

        Assert.Equal(new NotificationDispatchResult(1, 0), first);
        Assert.Equal(new NotificationDispatchResult(1, 0), second);
        Assert.Single(await Rows());
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task TheSameUserTwiceInAStepsTargetList_IsPagedOnce()
    {
        var email = new RecordingChannelDispatcher(NotificationType.Email);
        SeedPreference(push: false);

        var reached = await Sut(channels: [email]).NotifyUsersAsync([UserId, UserId], Payload(generation: 5));

        Assert.Equal(new NotificationDispatchResult(1, 0), reached);
        Assert.Single(await Rows());
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task ReopenedIncident_PagesTheSameUserAgain_UnderANewGeneration()
    {
        var email = new RecordingChannelDispatcher(NotificationType.Email);
        SeedPreference(push: false);
        var sut = Sut(channels: [email]);

        // Same incident, same user, same level-1 step — only EscalationStartedAt (and so the
        // generation) differs, exactly as it does after Incident.Reopen() restarts escalation.
        var firstRun = await sut.NotifyUsersAsync([UserId], Payload(generation: 100));
        var afterReopen = await sut.NotifyUsersAsync([UserId], Payload(generation: 200));

        Assert.Equal(new NotificationDispatchResult(1, 0), firstRun);
        Assert.Equal(new NotificationDispatchResult(1, 0), afterReopen);

        var rows = await Rows();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.DedupeKey).Distinct().Count());
        Assert.Equal(2, email.Sent.Count);
    }

    [Fact]
    public async Task PartiallyDedupedUser_StillCountsAsReached()
    {
        SeedPreference(push: false);
        var email = new RecordingChannelDispatcher(NotificationType.Email);
        var sms = new RecordingChannelDispatcher(NotificationType.Sms);

        // Round 1: email only. Round 2 adds SMS — the email row dedupes away but the SMS row
        // is a genuine page, so the user counts as reached.
        await Sut(channels: [email]).NotifyUsersAsync([UserId], Payload(generation: 7));
        _ctx.ChangeTracker.Clear();
        SeedPreferenceUpdateSmsOn();

        var reached = await Sut(channels: [email, sms]).NotifyUsersAsync([UserId], Payload(generation: 7));

        Assert.Equal(new NotificationDispatchResult(1, 0), reached);
        Assert.Single(email.Sent);
        Assert.Single(sms.Sent);
    }

    private void SeedPreferenceUpdateSmsOn()
    {
        var prefs = _ctx.NotificationPreferences.Single(p => p.UserId == UserId);
        prefs.SmsEnabled = true;
        _ctx.SaveChanges();
    }

    // ---- a store error is not an empty rota --------------------------------

    /// <summary>A failed claim comes back as a store failure, never as an empty rota.</summary>
    [Fact]
    public async Task ClaimFailure_ComesBackAsFailed_NotAsNobodyToPage()
    {
        SeedPreference(push: false);

        await using var failing = new FailingDbContext(Options(_dbName));

        var result = await Sut(channels: [new RecordingChannelDispatcher(NotificationType.Email)], ctx: failing)
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(0, result.Reached);
        Assert.Equal(1, result.Failed);
        Assert.True(result.DispatchFailed);
        Assert.False(result.NobodyToPage, "a store error must never be reported as an empty rota");
        Assert.Equal(NotificationType.Email, Assert.Single(result.ChannelFailures!).Channel);
        Assert.Empty(await Rows());
    }

    /// <summary>One channel's failed claim is reported even when the user was paged on another.</summary>
    [Fact]
    public async Task AChannelWhoseClaimFails_IsReported_EvenWhenTheUserWasPagedOnAnother()
    {
        SeedPreference(voice: true, push: false);

        var email = new RecordingChannelDispatcher(NotificationType.Email);
        var voice = new RecordingChannelDispatcher(NotificationType.VoiceCall);

        await using var flaky = new ChannelFailingDbContext(Options(_dbName), NotificationType.VoiceCall);

        var result = await Sut(channels: [email, voice], ctx: flaky).NotifyUsersAsync([UserId], Payload());

        Assert.Equal(1, result.Reached);
        Assert.Equal(0, result.Failed);
        Assert.False(result.DispatchFailed);
        Assert.False(result.NobodyToPage);

        Assert.True(result.PartiallyFailed, "a lost voice call must not be folded away into 'reached'");
        var lost = Assert.Single(result.ChannelFailures!);
        Assert.Equal(NotificationType.VoiceCall, lost.Channel);
        Assert.Equal(UserId, lost.UserId);

        Assert.Single(email.Sent);
        Assert.Empty(voice.Sent);
    }

    /// <summary>A genuinely empty rota reports itself as such and never as a dispatch failure.</summary>
    [Fact]
    public async Task NobodyToPage_AndDispatchFailed_AreMutuallyExclusive()
    {
        var result = await Sut(onCall: OnCall(primary: null)).NotifyOnCallAsync(ScheduleId, Payload());

        Assert.True(result.NobodyToPage);
        Assert.False(result.DispatchFailed);
        Assert.False(result.TargetsUnpageable);
        Assert.False(result.ChannelsSilent);
    }

    // ---- a claimed row is not a page ---------------------------------------

    /// <summary>A postponed page counts as reached and is reported as queued rather than sent.</summary>
    [Fact]
    public async Task ADeferredPage_CountsAsReached_ButIsReportedAsNotYetSent()
    {
        SeedPreference(email: false, voice: true, push: false);

        var result = await Sut(channels: [new RecordingChannelDispatcher(NotificationType.VoiceCall, ChannelOutcome.NoProvider)])
            .NotifyUsersAsync([UserId], Payload());

        // The call is coming, so the step has something to wait for and must not stampede past it.
        Assert.Equal(1, result.Reached);
        Assert.False(result.ChannelsSilent, "a page the retry sweep owns has not fallen silent — it is early");
        Assert.False(result.NobodyToPage);
        Assert.Equal(0, result.Silent);

        // ...but nobody's phone has rung YET, and the operator is told exactly that.
        Assert.True(result.HasDeferredPages);
        var deferral = Assert.Single(result.Deferrals!);
        Assert.Equal(NotificationType.VoiceCall, deferral.Channel);
        Assert.Contains("provider", result.DescribeDeferrals(), StringComparison.OrdinalIgnoreCase);

        // ...and the page is not lost: the row is still in the retry sweep's window.
        var row = Assert.Single(await Rows());
        Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
        Assert.NotNull(row.NextRetryAt);
        Assert.True(row.PageIsOnItsWay);
        Assert.True(row.IsDeferredPage);
    }

    /// <summary>The same claim through the real voice dispatcher and registry, with no double in it.</summary>
    [Fact]
    public async Task AColdVoiceRegistry_KeepsThePageAlive_AndTheStepWaitsForIt()
    {
        SeedPreference(email: false, voice: true, push: false);

        var registry = Substitute.For<ICommunicationProviderRegistry>();
        registry.GetProvider(Arg.Any<CommunicationCapability>()).Returns((ICommunicationProvider?)null);

        var voice = new VoiceCallChannelDispatcher(
            _contacts,
            registry,
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<VoiceCallChannelDispatcher>.Instance,
            new CallLogRepository(_ctx, NullLogger<CallLogRepository>.Instance));

        var result = await Sut(channels: [voice]).NotifyUsersAsync([UserId], Payload());

        // A call is queued and budgeted, so the step has something in flight.
        Assert.Equal(1, result.Reached);
        Assert.False(result.ChannelsSilent);
        Assert.False(result.NobodyToPage, "the rota is fine — the voice provider is the thing that is missing");

        // ...and it has NOT been placed, which is the fact the timeline has to carry.
        Assert.True(result.HasDeferredPages);

        // ...and the page itself is alive, on a short leash, waiting for the registry to fill.
        var row = Assert.Single(await Rows());
        Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
        Assert.NotNull(row.NextRetryAt);
        Assert.Equal(0, row.RetryCount);
        Assert.True(row.PageIsOnItsWay);
    }

    /// <summary>A provider that never appeared is a permanently silent channel, not a deferred page.</summary>
    [Fact]
    public async Task AProviderThatNeverAppeared_IsFinallyASilentChannel_NotADeferredPage()
    {
        SeedPreference(email: false, voice: true, push: false);

        var result = await Sut(channels: [new RecordingChannelDispatcher(NotificationType.VoiceCall, ChannelOutcome.Skipped)])
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.ChannelsSilent);
        Assert.False(result.HasDeferredPages, "a Skipped row is not coming back — nothing is deferred");
        Assert.True(result.AllSilencesPermanent);
        Assert.True(Assert.Single(result.ChannelSilences!).IsPermanent);
    }

    /// <summary>
    /// The same rule for a channel that is off for good. "Skipped means nothing was sent and nothing
    /// ever will be" — so it cannot also mean "this user was paged".
    /// </summary>
    [Fact]
    public async Task ASkippedChannel_DoesNotCountAsReached()
    {
        SeedPreference(push: false);

        var result = await Sut(channels: [new RecordingChannelDispatcher(NotificationType.Email, ChannelOutcome.Skipped)])
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.ChannelsSilent, "SMTP being unconfigured is a channel fault, not an empty rota");
        Assert.False(result.NobodyToPage);

        // "Nothing was sent and nothing ever will be" — so the row is out of the retry sweep's window
        // for good, and it claims no page.
        var row = Assert.Single(await Rows());
        Assert.Equal(NotificationDeliveryStatus.Skipped, row.DeliveryStatus);
        Assert.Null(row.NextRetryAt);
        Assert.False(row.PageIsOnItsWay);
    }

    /// <summary>A refused page still counts as reached, because the retry sweep owns the row.</summary>
    [Fact]
    public async Task AChannelWhoseProviderRefused_StillCountsAsReached_BecauseTheRetryOwnsIt()
    {
        SeedPreference(push: false);

        var result = await Sut(channels: [new RecordingChannelDispatcher(NotificationType.Email, ChannelOutcome.Failed)])
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(1, result.Reached);
        Assert.False(result.NobodyToPage);

        var row = Assert.Single(await Rows());
        Assert.Equal(NotificationDeliveryStatus.Failed, row.DeliveryStatus);
        Assert.NotNull(row.NextRetryAt);
        Assert.True(row.PageIsOnItsWay);
    }

    /// <summary>
    /// ...and a channel that stopped for good (the user has no address on it at all) reaches nobody, in
    /// the direction that has no second chance: nothing retries a PermanentlyFailed row either.
    /// </summary>
    [Fact]
    public async Task AChannelThatFailedPermanently_DoesNotCountAsReached()
    {
        SeedPreference(push: false);

        var result = await Sut(channels: [new RecordingChannelDispatcher(NotificationType.Email, ChannelOutcome.Permanent)])
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.ChannelsSilent);
        Assert.False(result.NobodyToPage);

        var row = Assert.Single(await Rows());
        Assert.Equal(NotificationDeliveryStatus.PermanentlyFailed, row.DeliveryStatus);
        Assert.Null(row.NextRetryAt);
        Assert.False(row.PageIsOnItsWay);
    }

    /// <summary>A user paged on one channel is reached even when another channel sent nothing.</summary>
    [Fact]
    public async Task AUserPagedOnOneChannel_IsReached_EvenIfAnotherChannelSentNothing()
    {
        SeedPreference(voice: true, push: false);

        // Skipped, not NoProvider: a channel that is silent FOR GOOD. A NoProvider row is merely
        // postponed — the sweep still owns it — and postponement is not silence (see
        // ADeferredPage_CountsAsReached_ButIsReportedAsNotYetSent).
        var result = await Sut(channels:
            [
                new RecordingChannelDispatcher(NotificationType.Email),
                new RecordingChannelDispatcher(NotificationType.VoiceCall, ChannelOutcome.Skipped)
            ])
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(1, result.Reached);
        Assert.False(result.NobodyToPage);
        Assert.False(result.ChannelsSilent);

        // ...but the dead voice channel is not folded away either: the step advanced, and this is the
        // only place the operator can learn that this responder's phone will never ring.
        Assert.True(result.PartiallySilent);
        Assert.Equal(NotificationType.VoiceCall, Assert.Single(result.ChannelSilences!).Channel);
    }

    // ---- push is not a page (these run with push ON, the production default) ----

    /// <summary>No voice provider and no SMTP reaches nobody, however many push rows were delivered.</summary>
    [Fact]
    public async Task NoVoiceProvider_NoSmtp_AndPushOn_ReachesNobody()
    {
        SeedPreference(email: true, voice: true, push: true);
        var push = new RecordingPushService();

        // Both channels are silent FOR GOOD — an SMTP nobody configured, and a voice provider that has
        // not appeared in half an hour (the point at which PhoneChannelDispatcher gives up and writes
        // Skipped). A merely-postponed voice row would be a page on its way, and would rightly rescue
        // the step; what must never rescue it is the push.
        var result = await Sut(
                channels:
                [
                    new RecordingChannelDispatcher(NotificationType.Email, ChannelOutcome.Skipped),
                    new RecordingChannelDispatcher(NotificationType.VoiceCall, ChannelOutcome.Skipped)
                ],
                push: push)
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.ChannelsSilent, "no provider and no SMTP is a silent channel, not a delivery");
        Assert.False(result.NobodyToPage, "the responder is on call — the CHANNELS are the fault");
        Assert.False(result.DispatchFailed);

        // The push still went out — the UI is better for it — and it was still written to the store...
        Assert.Equal([UserId], push.Pushed);
        var pushRow = Assert.Single(await Rows(), r => r.Type == NotificationType.Push);
        Assert.Equal(NotificationDeliveryStatus.Delivered, pushRow.DeliveryStatus);

        // ...and it counted for NOTHING, because a browser toast cannot wake anybody.
        Assert.False(pushRow.CountsAsReached);
        Assert.DoesNotContain(NotificationType.Push, result.ChannelSilences!.Select(s => s.Channel));
    }

    /// <summary>A push-only responder is unpageable, and the reason names what the operator must change.</summary>
    [Fact]
    public async Task AUserWithOnlyPushEnabled_IsUnpageable_NotAnEmptyRota()
    {
        SeedPreference(email: false, sms: false, voice: false, push: true);
        var push = new RecordingPushService();

        var result = await Sut(push: push).NotifyUsersAsync([UserId], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.TargetsUnpageable);
        Assert.False(result.NobodyToPage, "the responder is ON the rota — the rota is not empty");
        Assert.False(result.ChannelsSilent, "no paging channel was even asked — nothing fell silent");

        // ...and the reason names the thing the operator has to change.
        var target = Assert.Single(result.UnpageableTargets!);
        Assert.Equal(UserId, target.UserId);
        Assert.Contains("e-mail", target.Reason, StringComparison.OrdinalIgnoreCase);

        // The toast is still sent. It just never pretended to be a page.
        Assert.Equal([UserId], push.Pushed);
        Assert.Equal(NotificationType.Push, Assert.Single(await Rows()).Type);
    }

    /// <summary>An on-call responder with voice enabled and no phone number is unpageable, not an empty rota.</summary>
    [Fact]
    public async Task AnOnCallResponderWithNoPhoneNumber_IsUnpageable_NotAnEmptyRota()
    {
        WithContact(phone: null);
        SeedPreference(email: false, sms: true, voice: true, push: false);

        var result = await Sut(channels:
            [
                new RecordingChannelDispatcher(NotificationType.Sms),
                new RecordingChannelDispatcher(NotificationType.VoiceCall)
            ])
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.TargetsUnpageable);
        Assert.False(result.NobodyToPage, "this responder is on call — they just have no phone number");

        Assert.Contains("phone number", Assert.Single(result.UnpageableTargets!).Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await Rows());
    }

    /// <summary>A delivered push cannot make a silent paging channel count as reached.</summary>
    [Fact]
    public async Task ADeliveredPush_CannotMakeASilentPagingChannel_CountAsReached()
    {
        SeedPreference(email: true, push: true);

        var result = await Sut(
                channels: [new RecordingChannelDispatcher(NotificationType.Email, ChannelOutcome.Skipped)],
                push: new RecordingPushService())
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.ChannelsSilent);
    }

    /// <summary>
    /// ...and with push on, a real page still reads as a page. The fix must not cost us the reached
    /// count we legitimately had — that would walk the escalation past a responder who WAS woken.
    /// </summary>
    [Fact]
    public async Task WithPushOn_ARealPagingChannel_StillCountsAsReached()
    {
        SeedPreference(email: true, push: true);

        var result = await Sut(
                channels: [new RecordingChannelDispatcher(NotificationType.Email)],
                push: new RecordingPushService())
            .NotifyUsersAsync([UserId], Payload());

        Assert.Equal(1, result.Reached);
        Assert.False(result.ChannelsSilent);
        Assert.False(result.NobodyToPage);
    }

    /// <summary>A push that throws is stored as skipped, so the row is the truth about what happened.</summary>
    [Fact]
    public async Task APushThatThrows_IsRecordedAsSkipped_NotAsDelivered()
    {
        SeedPreference(email: true, push: true);
        var push = new RecordingPushService { Throws = new InvalidOperationException("hub is gone") };

        var result = await Sut(
                channels: [new RecordingChannelDispatcher(NotificationType.Email)],
                push: push)
            .NotifyUsersAsync([UserId], Payload());

        var pushRow = Assert.Single(await Rows(), r => r.Type == NotificationType.Push);
        Assert.Equal(NotificationDeliveryStatus.Skipped, pushRow.DeliveryStatus);
        Assert.False(pushRow.PageIsOnItsWay);

        // The e-mail still paged them, and a dead toast does not un-page a responder.
        Assert.Equal(1, result.Reached);
    }

    // ---- paging the on-call rota -------------------------------------------

    private const string Primary = "primary-1";
    private const string Secondary = "secondary-1";
    private static readonly Guid ScheduleId = Guid.NewGuid();

    private IOnCallService OnCall(string? primary, string? secondary = null)
    {
        var service = Substitute.For<IOnCallService>();
        service.GetCurrentOnCallAsync(ScheduleId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(primary is null && secondary is null
                ? null
                : new OnCallStatusDto
                {
                    ScheduleId = ScheduleId,
                    ScheduleName = "Primary rota",
                    PrimaryUserId = primary,
                    SecondaryUserId = secondary
                });

        foreach (var userId in new[] { primary, secondary }.OfType<string>())
            _contacts.GetContactByIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(new UserContactSnapshot(userId, userId, "+905550000000", $"{userId}@example.io"));

        return service;
    }

    private async Task<string[]> PagedUserIdsAsync() =>
        (await Rows()).Select(n => n.UserId).Distinct().Order().ToArray();

    /// <summary>An empty rota pages nobody and reports it, because the orchestrator escalates off that answer.</summary>
    [Fact]
    public async Task NotifyOnCall_WithAnEmptyRota_PagesNobody_AndSaysSo()
    {
        var result = await Sut(onCall: OnCall(primary: null)).NotifyOnCallAsync(ScheduleId, Payload());

        Assert.True(result.NobodyToPage);
        Assert.Empty(await Rows());
    }

    /// <summary>The same unpageable/empty-rota distinction through the schedule path.</summary>
    [Fact]
    public async Task NotifyOnCall_WhereTheOnCallUserHasNoContactRecord_IsUnpageable_NotAnEmptyRota()
    {
        var onCall = Substitute.For<IOnCallService>();
        onCall.GetCurrentOnCallAsync(ScheduleId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new OnCallStatusDto
            {
                ScheduleId = ScheduleId,
                ScheduleName = "Primary rota",
                PrimaryUserId = "ghost-oncall"
            });
        _contacts.GetContactByIdAsync("ghost-oncall", Arg.Any<CancellationToken>())
            .Returns((UserContactSnapshot?)null);

        var result = await Sut(onCall: onCall).NotifyOnCallAsync(ScheduleId, Payload());

        Assert.Equal(0, result.Reached);
        Assert.True(result.TargetsUnpageable);
        Assert.False(result.NobodyToPage, "the schedule HAS an on-call responder — the rota is not empty");
        Assert.Equal("ghost-oncall", Assert.Single(result.UnpageableTargets!).UserId);
        Assert.Empty(await Rows());
    }

    /// <summary>The ordinary case: the person on call is paged, and only them.</summary>
    [Fact]
    public async Task NotifyOnCall_PagesThePersonOnCall()
    {
        var result = await Sut(onCall: OnCall(Primary, Secondary)).NotifyOnCallAsync(ScheduleId, Payload());

        Assert.False(result.NobodyToPage);
        Assert.Equal([Primary], await PagedUserIdsAsync());
    }

    /// <summary>The secondary is left alone unless the step opted in.</summary>
    [Fact]
    public async Task NotifyOnCall_WithoutTheOptIn_LeavesTheSecondaryAlone()
    {
        await Sut(onCall: OnCall(Primary, Secondary))
            .NotifyOnCallAsync(ScheduleId, Payload() with { IncludeSecondaryOnCall = false });

        Assert.DoesNotContain(Secondary, await PagedUserIdsAsync());
    }

    /// <summary>And when the step DOES ask for both, both phones ring.</summary>
    [Fact]
    public async Task NotifyOnCall_WithTheOptIn_PagesPrimaryAndSecondary()
    {
        var result = await Sut(onCall: OnCall(Primary, Secondary))
            .NotifyOnCallAsync(ScheduleId, Payload() with { IncludeSecondaryOnCall = true });

        Assert.False(result.NobodyToPage);
        Assert.Equal([Primary, Secondary], await PagedUserIdsAsync());
    }

    /// <summary>An unfilled secondary slot must not take the primary down with it.</summary>
    [Fact]
    public async Task NotifyOnCall_WithTheOptIn_ButNoSecondaryOnCall_StillPagesThePrimary()
    {
        var result = await Sut(onCall: OnCall(Primary))
            .NotifyOnCallAsync(ScheduleId, Payload() with { IncludeSecondaryOnCall = true });

        Assert.False(result.NobodyToPage);
        Assert.Equal([Primary], await PagedUserIdsAsync());
    }

    // ---- the "send test notification" button --------------------------------

    [Fact]
    public async Task SendTest_ToAnUnknownUser_FailsWithAReason()
    {
        var (success, message) = await Sut().SendTestNotificationAsync("nobody", "email");

        Assert.False(success);
        Assert.Contains("not found", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTest_OnAnUnknownChannel_FailsWithAReason()
    {
        var (success, message) = await Sut().SendTestNotificationAsync(UserId, "carrier-pigeon");

        Assert.False(success);
        Assert.Contains("carrier-pigeon", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A channel with no registered dispatcher fails the test send rather than reporting success.</summary>
    [Fact]
    public async Task SendTest_OnAChannelWithNoDispatcher_FailsWithAReason()
    {
        var (success, message) = await Sut(channels: [new RecordingChannelDispatcher(NotificationType.Email)])
            .SendTestNotificationAsync(UserId, "voice");

        Assert.False(success);
        Assert.Contains("voice", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTest_OnAWiredChannel_ReachesThatChannelsDispatcher()
    {
        var email = new RecordingChannelDispatcher(NotificationType.Email);

        var (success, _) = await Sut(channels: [email]).SendTestNotificationAsync(UserId, "EMAIL");

        Assert.True(success);
        Assert.Equal([UserId], email.TestsSent);
    }

    private const string SecondaryUserId = "user-2";

    private IOnCallService TeamOnCall(bool withSecondary)
    {
        var onCall = Substitute.For<IOnCallService>();
        onCall.GetCurrentOnCallForTeamAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new OnCallStatusDto
            {
                PrimaryUserId = UserId,
                SecondaryUserId = withSecondary ? SecondaryUserId : null
            });
        return onCall;
    }

    [Fact]
    public async Task NotifyTeamOnCall_WithNotifyBothOnCall_PagesTheSecondaryToo()
    {
        _contacts.GetContactByIdAsync(SecondaryUserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(SecondaryUserId, "Secondary", "+905550000000", "user2@example.io"));
        var email = new RecordingChannelDispatcher(NotificationType.Email);

        var reached = await Sut(channels: [email], onCall: TeamOnCall(withSecondary: true))
            .NotifyTeamAsync(Guid.NewGuid(), Payload(includeSecondary: true), notifyAllMembers: false);

        Assert.Equal(new NotificationDispatchResult(2, 0), reached);
        Assert.Equal(
            new[] { UserId, SecondaryUserId },
            email.Sent.Select(n => n.UserId).Distinct().OrderBy(id => id));
    }

    [Fact]
    public async Task NotifyOnCall_ReadsTheOnCallStatusUncached_SoAHandoverIsNotStale()
    {
        var onCall = Substitute.For<IOnCallService>();
        onCall.GetCurrentOnCallAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new OnCallStatusDto { PrimaryUserId = UserId });

        await Sut(onCall: onCall).NotifyOnCallAsync(Guid.NewGuid(), Payload());

        await onCall.Received(1).GetCurrentOnCallAsync(Arg.Any<Guid>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyTeam_ReadsTheOnCallStatusUncached()
    {
        var onCall = TeamOnCall(withSecondary: false);

        await Sut(onCall: onCall).NotifyTeamAsync(Guid.NewGuid(), Payload(), notifyAllMembers: false);

        await onCall.Received(1).GetCurrentOnCallForTeamAsync(Arg.Any<Guid>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyTeamOnCall_WithoutTheFlag_PagesOnlyThePrimary()
    {
        _contacts.GetContactByIdAsync(SecondaryUserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(SecondaryUserId, "Secondary", "+905550000000", "user2@example.io"));
        var email = new RecordingChannelDispatcher(NotificationType.Email);

        var reached = await Sut(channels: [email], onCall: TeamOnCall(withSecondary: true))
            .NotifyTeamAsync(Guid.NewGuid(), Payload(includeSecondary: false), notifyAllMembers: false);

        Assert.Equal(new NotificationDispatchResult(1, 0), reached);
        Assert.Equal([UserId], email.Sent.Select(n => n.UserId).Distinct());
    }
}
