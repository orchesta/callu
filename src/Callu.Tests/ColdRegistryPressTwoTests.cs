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
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Drives a responder pressing 2 through the real orchestrator, dispatcher and voice channel against a cold provider registry.</summary>
public class ColdRegistryPressTwoTests : IDisposable
{
    private const string Responder = "responder-2";

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"cold-registry-{Guid.NewGuid():N}").Options);

    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public ColdRegistryPressTwoTests() =>
        _contacts.GetContactByIdAsync(Responder, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(Responder, "Responder Two", "+905551112233", "r2@example.io"));

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Pressing 2 with a reloading registry still queues the call, and the keypress says so.</summary>
    [Fact]
    public async Task PressTwo_WhenTheVoiceRegistryIsCold_StillQueuesTheCall_AndSaysSo()
    {
        var incidentId = await SeedEscalatingIncidentAsync();

        var pagedSomeone = await Orchestrator(registryHasProvider: false).EscalateNowAsync(incidentId);

        Assert.True(pagedSomeone,
            "the call to the next responder is queued with its full retry budget and the sweep will dial it — "
            + "reporting 'nobody was paged' here is what let the escalation treat the step as having nothing in "
            + "flight, backdate its clock, and exhaust the whole policy in thirty seconds");
    }

    /// <summary>The page stays in the retry sweep's window with its budget intact rather than being written off.</summary>
    [Fact]
    public async Task PressTwo_WithAColdRegistry_KeepsThePageAlive_RatherThanWritingItOff()
    {
        var incidentId = await SeedEscalatingIncidentAsync();

        await Orchestrator(registryHasProvider: false).EscalateNowAsync(incidentId);

        var row = Assert.Single(await _ctx.Notifications.AsNoTracking().ToListAsync());

        Assert.Equal(NotificationType.VoiceCall, row.Type);
        Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
        Assert.NotNull(row.NextRetryAt);
        Assert.Equal(0, row.RetryCount);
        Assert.True(row.PageIsOnItsWay);
        Assert.True(row.IsDeferredPage);

        // Skipped and PermanentlyFailed are both OUTSIDE the sweep's window — either one here is the
        // page dying over a question no provider was ever asked.
        Assert.NotEqual(NotificationDeliveryStatus.Skipped, row.DeliveryStatus);
        Assert.NotEqual(NotificationDeliveryStatus.PermanentlyFailed, row.DeliveryStatus);
    }

    /// <summary>The timeline says the call is queued rather than placed, and does not blame the rota for it.</summary>
    [Fact]
    public async Task PressTwo_WithAColdRegistry_TellsTheTimeline_ThatTheCallIsQueuedNotPlaced()
    {
        var incidentId = await SeedEscalatingIncidentAsync();

        await Orchestrator(registryHasProvider: false).EscalateNowAsync(incidentId);

        var timeline = await _ctx.Set<IncidentTimelineEvent>()
            .Where(e => e.IncidentId == incidentId)
            .ToListAsync();

        var queued = Assert.Single(timeline, e => e.Title.Contains("queued, not yet sent", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("VoiceCall", queued.Description!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("provider", queued.Description!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30 minutes", queued.Description!, StringComparison.OrdinalIgnoreCase);

        // The rota is never blamed for a provider that is warming up.
        Assert.DoesNotContain(timeline, e => e.Description is not null
            && e.Description.Contains("has no on-call responder", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The same keypress with push on and the voice channel dead for good still pages nobody, and says so.</summary>
    [Fact]
    public async Task PressTwo_WithPushEnabled_AndADeadVoiceChannel_StillPagesNobody_AndSaysSo()
    {
        var incidentId = await SeedEscalatingIncidentAsync(pushEnabled: true);
        var push = new RecordingPushService();

        // No provider has appeared for over the 30-minute window, so the voice row is Skipped: nothing
        // was sent and nothing ever will be. All that is left is the push.
        var pagedSomeone = await Orchestrator(registryHasProvider: false, push: push, voiceIsDeadForGood: true)
            .EscalateNowAsync(incidentId);

        Assert.False(pagedSomeone,
            "the voice channel is dead and NOBODY WAS CALLED, but a browser toast was 'delivered' — and "
            + "that used to be enough to tell the responder on the phone that help was on the way");

        // The push really did go out (the UI wants it) and really was stored as delivered...
        Assert.Equal([Responder], push.Pushed);
        var pushRow = Assert.Single(await _ctx.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.Push).ToListAsync());
        Assert.Equal(NotificationDeliveryStatus.Delivered, pushRow.DeliveryStatus);

        // ...and it counted for nothing, because it cannot wake anybody.
        Assert.False(pushRow.CountsAsReached);

        // The timeline tells the truth rather than reporting a triggered step.
        Assert.Contains(
            await _ctx.Set<IncidentTimelineEvent>().Where(e => e.IncidentId == incidentId).ToListAsync(),
            e => e.Title.Contains("nobody could be paged", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The control: with a warm registry the same chain places the call, so the harness can page.</summary>
    [Fact]
    public async Task PressTwo_WithAWarmRegistry_ActuallyPlacesTheCall_AndComesBackTrue()
    {
        var incidentId = await SeedEscalatingIncidentAsync();

        var pagedSomeone = await Orchestrator(registryHasProvider: true).EscalateNowAsync(incidentId);

        Assert.True(pagedSomeone);

        var row = Assert.Single(await _ctx.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(NotificationDeliveryStatus.Delivered, row.DeliveryStatus);
        Assert.True(row.PageIsOnItsWay);
    }

    // ── harness: the real orchestrator, over the real dispatcher, over the real voice channel ─────

    // voiceIsDeadForGood stands in for the end of the provider-unavailable window: the voice row is already Skipped.
    private EscalationOrchestrator Orchestrator(
        bool registryHasProvider,
        INotificationPushService? push = null,
        bool voiceIsDeadForGood = false)
    {
        var registry = Substitute.For<ICommunicationProviderRegistry>();

        if (registryHasProvider)
        {
            var provider = Substitute.For<ICommunicationProvider>();
            provider.MakeCallAsync(Arg.Any<MakeCallRequest>()).Returns(new CallResult { Success = true });
            registry.GetProvider(Arg.Any<CommunicationCapability>()).Returns(provider);
        }
        else
        {
            // A registry still loading, stated rather than defaulted: an empty registry that HAS loaded
            // is a different, permanent answer.
            registry.GetProvider(Arg.Any<CommunicationCapability>()).Returns((ICommunicationProvider?)null);
            registry.DescribeAbsence(Arg.Any<CommunicationCapability>()).Returns(ProviderAbsence.RegistryNotLoaded);
        }

        INotificationChannelDispatcher voice = voiceIsDeadForGood
            ? new RecordingChannelDispatcher(NotificationType.VoiceCall, ChannelOutcome.Skipped)
            : new VoiceCallChannelDispatcher(
                _contacts,
                registry,
                new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
                new CalluMetrics(new FakeMeterFactory()),
                NullLogger<VoiceCallChannelDispatcher>.Instance,
                new CallLogRepository(_ctx, NullLogger<CallLogRepository>.Instance));

        var settings = Substitute.For<IOrganizationSettingsService>();
        settings.GetPublicBaseUrlAsync(Arg.Any<CancellationToken>()).Returns("https://callu.example.io");

        var notifications = new NotificationDispatcher(
            new NotificationRepository(_ctx, NullLogger<NotificationRepository>.Instance),
            new NotificationPreferenceRepository(_ctx, NullLogger<NotificationPreferenceRepository>.Instance),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            new ImmediateTransactionManager(),
            _contacts,
            Substitute.For<IOnCallService>(),
            settings,
            DateTimeZoneProviders.Tzdb,
            new FixedClock(Instant.FromUtc(2026, 7, 14, 12, 0)),
            [voice],
            _ctx,
            NullLogger<NotificationDispatcher>.Instance,
            push);

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

    /// <summary>An open incident mid-escalation whose next step pages one voice-only responder.</summary>
    private async Task<Guid> SeedEscalatingIncidentAsync(bool pushEnabled = false)
    {
        var step = new EscalationStep
        {
            Id = Guid.NewGuid(),
            Level = 1,
            DelayMinutes = 0,
            TargetedUsers = new List<EscalationStepUser> { new() { UserId = Responder } }
        };

        var policy = new EscalationPolicy
        {
            Id = Guid.NewGuid(),
            Name = "Payments policy",
            IsActive = true,
            Steps = [step]
        };

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.Open,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        _ctx.Add(policy);
        _ctx.Add(incident);
        _ctx.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = Responder,
            EmailEnabled = false,
            SmsEnabled = false,
            VoiceEnabled = true,
            PushEnabled = pushEnabled,
            Timezone = "UTC"
        });

        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        return incident.Id;
    }
}
