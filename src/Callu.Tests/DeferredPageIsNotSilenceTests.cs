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
using Callu.Shared.Models.Schedules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Drives the whole escalation chain against a cold provider registry, where a deferred page used to read as silence.</summary>
public class DeferredPageIsNotSilenceTests : IDisposable
{
    private const string OnCallResponder = "responder-oncall";
    private static readonly Guid ScheduleId = Guid.NewGuid();

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"ladder-{Guid.NewGuid():N}").Options);

    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public DeferredPageIsNotSilenceTests() =>
        _contacts.GetContactByIdAsync(OnCallResponder, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(OnCallResponder, "On-call", "+905551112233", "oncall@example.io"));

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// FIVE Quartz ticks, back to back, with a cold registry — more ticks than the whole policy used to
    /// survive. The escalation must still be alive on step 1, waiting for the call it queued.
    /// </summary>
    [Fact]
    public async Task AColdRegistry_DoesNotExhaustAThreeStepPolicy_InThirtySeconds()
    {
        var incidentId = await SeedIncidentWithThreeScheduleStepsAsync();
        var orchestrator = Orchestrator();

        for (var tick = 0; tick < 5; tick++)
            await orchestrator.ProcessPendingEscalationsAsync();

        var incident = await _ctx.Incidents.AsNoTracking().SingleAsync(i => i.Id == incidentId);

        Assert.True(incident.IsEscalationActive,
            "the escalation ran to exhaustion without a single call being dialled: a deferred page was read as a "
            + "silent channel, so every step believed it had nothing in flight to wait for and fired the next one "
            + "on the following tick");

        var steps = await OrderedStepsAsync();
        Assert.Equal(steps[0].Id, incident.CurrentEscalationStepId);
    }

    /// <summary>The same five ticks counted at the phone: one page to one person, not one per step.</summary>
    [Fact]
    public async Task AColdRegistry_QueuesOnePageForTheOnCallResponder_NotOnePerStep()
    {
        await SeedIncidentWithThreeScheduleStepsAsync();
        var orchestrator = Orchestrator();

        for (var tick = 0; tick < 5; tick++)
            await orchestrator.ProcessPendingEscalationsAsync();

        var voicePages = await _ctx.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.VoiceCall && n.UserId == OnCallResponder)
            .ToListAsync();

        Assert.Single(voicePages);
    }

    /// <summary>The queued page is inside the retry sweep's window with its full budget, which is what earns the wait.</summary>
    [Fact]
    public async Task TheDeferredPage_IsInTheRetrySweepsWindow_AndKeepsItsFullBudget()
    {
        await SeedIncidentWithThreeScheduleStepsAsync();

        await Orchestrator().ProcessPendingEscalationsAsync();

        var page = await _ctx.Notifications.AsNoTracking()
            .SingleAsync(n => n.Type == NotificationType.VoiceCall);

        Assert.Equal(NotificationDeliveryStatus.Pending, page.DeliveryStatus);
        Assert.NotNull(page.NextRetryAt);
        Assert.Equal(0, page.RetryCount);

        Assert.True(page.RetrySweepWillTakeIt, "nothing will ever send this page");
        Assert.True(page.IsDeferredPage);
        Assert.True(page.PageIsOnItsWay);
    }

    /// <summary>A step reached only by a deferred page says on the timeline that no phone has rung yet.</summary>
    [Fact]
    public async Task AStepReachedOnlyByADeferredPage_SaysOnTheTimeline_ThatNoPhoneHasRungYet()
    {
        var incidentId = await SeedIncidentWithThreeScheduleStepsAsync();

        await Orchestrator().ProcessPendingEscalationsAsync();

        var timeline = await _ctx.Set<IncidentTimelineEvent>()
            .Where(e => e.IncidentId == incidentId)
            .ToListAsync();

        var queued = Assert.Single(timeline,
            e => e.Title.Contains("queued, not yet sent", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("VoiceCall", queued.Description!, StringComparison.OrdinalIgnoreCase);

        // ...and it never blames the rota, which is exactly where this responder IS.
        Assert.DoesNotContain(timeline, e => e.Description is not null
            && e.Description.Contains("has no on-call responder", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The control: a channel silent for good still advances the ladder, because nothing is coming.</summary>
    [Fact]
    public async Task AChannelThatIsSilentForGood_StillAdvancesTheLadder_BecauseNothingIsComing()
    {
        var incidentId = await SeedIncidentWithThreeScheduleStepsAsync();

        // Skipped: the 30-minute provider window is spent, or SMTP was never configured.
        var orchestrator = Orchestrator(
            voice: new RecordingChannelDispatcher(NotificationType.VoiceCall, ChannelOutcome.Skipped));

        for (var tick = 0; tick < 5; tick++)
            await orchestrator.ProcessPendingEscalationsAsync();

        var incident = await _ctx.Incidents.AsNoTracking().SingleAsync(i => i.Id == incidentId);

        Assert.False(incident.IsEscalationActive,
            "nothing was queued and nothing is coming, so the policy has to run out rather than wait on a page "
            + "that will never be sent");
    }

    // ── harness: the real orchestrator, over the real funnel, over the real voice channel ─────────

    private EscalationOrchestrator Orchestrator(INotificationChannelDispatcher? voice = null)
    {
        // The Worker's first seconds: the snapshot is empty because the first load has not finished yet —
        // a voice provider IS configured, it is simply not loaded. (An empty registry that has completed a
        // load means the opposite and is answered the opposite way; see
        // UnconfiguredChannelIsSilenceNotDelayTests.)
        var registry = Substitute.For<ICommunicationProviderRegistry>();
        registry.GetProvider(Arg.Any<CommunicationCapability>()).Returns((ICommunicationProvider?)null);
        registry.DescribeAbsence(Arg.Any<CommunicationCapability>()).Returns(ProviderAbsence.RegistryNotLoaded);

        voice ??= new VoiceCallChannelDispatcher(
            _contacts,
            registry,
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<VoiceCallChannelDispatcher>.Instance,
            new CallLogRepository(_ctx, NullLogger<CallLogRepository>.Instance));

        var settings = Substitute.For<IOrganizationSettingsService>();
        settings.GetPublicBaseUrlAsync(Arg.Any<CancellationToken>()).Returns("https://callu.example.io");

        var onCall = Substitute.For<IOnCallService>();
        onCall.GetCurrentOnCallAsync(ScheduleId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new OnCallStatusDto
            {
                ScheduleId = ScheduleId,
                ScheduleName = "Primary rota",
                PrimaryUserId = OnCallResponder
            });

        var notifications = new NotificationDispatcher(
            new NotificationRepository(_ctx, NullLogger<NotificationRepository>.Instance),
            new NotificationPreferenceRepository(_ctx, NullLogger<NotificationPreferenceRepository>.Instance),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            new ImmediateTransactionManager(),
            _contacts,
            onCall,
            settings,
            DateTimeZoneProviders.Tzdb,
            new FixedClock(Instant.FromUtc(2026, 7, 14, 12, 0)),
            [voice],
            _ctx,
            NullLogger<NotificationDispatcher>.Instance);

        return new EscalationOrchestrator(
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new EscalationPolicyRepository(_ctx, NullLogger<EscalationPolicyRepository>.Instance),
            new IncidentTimelineEventRepository(_ctx, NullLogger<IncidentTimelineEventRepository>.Instance),
            new CallLogRepository(_ctx, NullLogger<CallLogRepository>.Instance),
            Substitute.For<IAuditLogService>(),
            new SavingTransactionManager(_ctx),
            notifications,
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<EscalationOrchestrator>.Instance);
    }

    private async Task<List<EscalationStep>> OrderedStepsAsync() =>
        await _ctx.Set<EscalationStep>().AsNoTracking().OrderBy(s => s.Level).ToListAsync();

    /// <summary>The ordinary configuration: three steps, delays 0/2/3 minutes, all paging the same on-call schedule.</summary>
    private async Task<Guid> SeedIncidentWithThreeScheduleStepsAsync()
    {
        var policy = new EscalationPolicy
        {
            Id = Guid.NewGuid(),
            Name = "Payments policy",
            IsActive = true,
            Steps =
            [
                new EscalationStep { Id = Guid.NewGuid(), Level = 1, DelayMinutes = 0, ScheduleId = ScheduleId },
                new EscalationStep { Id = Guid.NewGuid(), Level = 2, DelayMinutes = 2, ScheduleId = ScheduleId },
                new EscalationStep { Id = Guid.NewGuid(), Level = 3, DelayMinutes = 3, ScheduleId = ScheduleId }
            ]
        };

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.Open,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };

        _ctx.Add(policy);
        _ctx.Add(incident);
        _ctx.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = OnCallResponder,
            EmailEnabled = false,
            SmsEnabled = false,
            VoiceEnabled = true,
            PushEnabled = false,
            Timezone = "UTC"
        });

        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        return incident.Id;
    }
}
