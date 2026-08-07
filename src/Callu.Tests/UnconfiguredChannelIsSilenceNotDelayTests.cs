using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>A provider that cannot be used yet earns a wait; a provider that does not exist earns silence at once.</summary>
public class UnconfiguredChannelIsSilenceNotDelayTests : IDisposable
{
    private const string Responder = "responder-sms";

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"unconfigured-{Guid.NewGuid():N}").Options);

    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public UnconfiguredChannelIsSilenceNotDelayTests() =>
        _contacts.GetContactByIdAsync(Responder, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(Responder, "Responder", "+905551112233", "r@example.io"));

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── the lie: a channel nobody configured, reported as a page on its way ───────────────────────

    /// <summary>An SMS page on an install with no SMS provider is silenced at once rather than queued.</summary>
    [Fact]
    public async Task AnSmsPage_OnAnInstallWithNoSmsProvider_IsSilencedAtOnce_NotQueued()
    {
        var notification = SmsPage();

        await Sms(ProviderAbsence.NotConfigured).SendAsync(notification, null, "+905551112233", TestPayload(), null);

        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
        Assert.False(notification.PageIsOnItsWay, "nothing was sent and nothing is coming — the step must not wait");
        Assert.False(notification.CountsAsReached);
        Assert.False(notification.RetrySweepWillTakeIt);
    }

    /// <summary>The retry path takes the same answer: re-asking a registry that has none is not a plan.</summary>
    [Fact]
    public async Task AnSmsRetry_OnAnInstallWithNoSmsProvider_IsSilencedAtOnce()
    {
        var incidentId = await SeedIncidentAsync();
        var notification = SmsPage(incidentId);

        await Sms(ProviderAbsence.NotConfigured).RetryAsync(notification, baseUrl: null);

        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
    }

    /// <summary>Pressing 2 for an SMS-only responder on an install with no SMS provider says nobody was paged.</summary>
    [Fact]
    public async Task PressTwo_ForAnSmsOnlyResponder_WithNoSmsProvider_TellsThemNobodyWasPaged()
    {
        var incidentId = await SeedEscalatingIncidentAsync();

        var pagedSomeone = await Orchestrator(ProviderAbsence.NotConfigured).EscalateNowAsync(incidentId);

        Assert.False(pagedSomeone,
            "no SMS provider is configured on this installation, so the page was never sent and never will be — "
            + "telling the responder an escalation was initiated is a lie they will act on by hanging up");

        var row = Assert.Single(await _ctx.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(NotificationDeliveryStatus.Skipped, row.DeliveryStatus);
        Assert.False(row.CountsAsReached);

        // The escalation is not left waiting on a page that is not coming, and the timeline says why.
        var timeline = await _ctx.Set<IncidentTimelineEvent>()
            .Where(e => e.IncidentId == incidentId).ToListAsync();

        Assert.Contains(timeline, e => e.Title.Contains("nobody could be paged", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(timeline, e => e.Title.Contains("queued, not yet sent", StringComparison.OrdinalIgnoreCase));

        // ...and it is not blamed on the rota. This responder IS on call; the SMS gateway is the fault.
        Assert.DoesNotContain(timeline, e => e.Description is not null
            && e.Description.Contains("has no on-call responder", StringComparison.OrdinalIgnoreCase));
    }

    // ── what must survive: the cold-registry win, on both channels ────────────────────────────────

    /// <summary>The line the fix must not cross: a page is never killed over a question no provider was asked.</summary>
    [Theory]
    [InlineData(ProviderAbsence.RegistryNotLoaded)]
    [InlineData(ProviderAbsence.ConfiguredButUnavailable)]
    public async Task AVoicePage_WhoseProviderIsMerelyNotUsableYet_StillWaits(ProviderAbsence absence)
    {
        var notification = VoicePage();

        await Voice(absence).SendAsync(notification, null, "+905551112233", TestPayload(), null);

        Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
        Assert.NotNull(notification.NextRetryAt);
        Assert.Equal(0, notification.RetryCount);
        Assert.True(notification.PageIsOnItsWay);
        Assert.True(notification.RetrySweepWillTakeIt);
    }

    /// <summary>A configured SMS gateway whose initialisation threw still waits, because the distinction is not per channel.</summary>
    [Fact]
    public async Task AnSmsPage_WhoseConfiguredGatewayFailedToInitialize_StillWaits()
    {
        var notification = SmsPage();

        await Sms(ProviderAbsence.ConfiguredButUnavailable)
            .SendAsync(notification, null, "+905551112233", TestPayload(), null);

        Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
        Assert.NotNull(notification.NextRetryAt);
        Assert.True(notification.PageIsOnItsWay);
    }

    /// <summary>
    /// ...but a provider that stays broken cannot re-queue the page forever. Once the wall-clock window is
    /// spent, "nothing was sent and nothing ever will be" is finally true and Skipped is finally honest.
    /// </summary>
    [Fact]
    public async Task AConfiguredProviderThatStaysBroken_EventuallyStops()
    {
        var incidentId = await SeedIncidentAsync();
        var notification = SmsPage(incidentId);
        notification.CreatedAt =
            DateTime.UtcNow - PhoneChannelDispatcher.ProviderUnavailableWindow - TimeSpan.FromMinutes(1);

        await Sms(ProviderAbsence.ConfiguredButUnavailable).RetryAsync(notification, baseUrl: null);

        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
    }

    // ── the registry actually answers this, rather than a substitute doing it ─────────────────────

    /// <summary>A fresh registry reports that it has not loaded, which is the default that keeps a page alive.</summary>
    [Fact]
    public void AFreshRegistry_HasNotLoaded_AndSaysSo_RatherThanClaimingNothingIsConfigured()
    {
        var registry = new CommunicationProviderRegistry(
            Substitute.For<IServiceProvider>(),
            Microsoft.Extensions.Options.Options.Create(
                new Infrastructure.Configuration.CommunicationSettingsOptions()),
            NullLogger<CommunicationProviderRegistry>.Instance);

        Assert.Null(registry.GetProvider(CommunicationCapability.Sms));

        Assert.Equal(ProviderAbsence.RegistryNotLoaded, registry.DescribeAbsence(CommunicationCapability.Sms));
        Assert.Equal(ProviderAbsence.RegistryNotLoaded, registry.DescribeAbsence(CommunicationCapability.VoiceCalls));

        Assert.Equal(ProviderAbsence.RegistryNotLoaded, default(ProviderAbsence));
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────

    private static NotificationPayload TestPayload() => new()
    {
        IncidentId = Guid.NewGuid(),
        Title = "Checkout failing",
        Severity = "Critical",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1
    };

    private static Notification SmsPage(Guid? incidentId = null) => Page(NotificationType.Sms, incidentId);
    private static Notification VoicePage(Guid? incidentId = null) => Page(NotificationType.VoiceCall, incidentId);

    private static Notification Page(NotificationType type, Guid? incidentId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Responder,
        Type = type,
        Title = "Escalation step 1",
        IncidentId = incidentId,
        DeliveryStatus = NotificationDeliveryStatus.Sending,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// A registry with NO provider for anything, that explains itself. This is the shape the real one
    /// takes on a voice-first install when it is asked for SMS.
    /// </summary>
    private static ICommunicationProviderRegistry Registry(ProviderAbsence absence)
    {
        var registry = Substitute.For<ICommunicationProviderRegistry>();
        registry.GetProvider(Arg.Any<CommunicationCapability>()).Returns((ICommunicationProvider?)null);
        registry.DescribeAbsence(Arg.Any<CommunicationCapability>()).Returns(absence);
        return registry;
    }

    private SmsChannelDispatcher Sms(ProviderAbsence absence) =>
        new SmsChannelDispatcher(
            _contacts,
            Registry(absence),
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<SmsChannelDispatcher>.Instance);

    private VoiceCallChannelDispatcher Voice(ProviderAbsence absence) =>
        new VoiceCallChannelDispatcher(
            _contacts,
            Registry(absence),
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<VoiceCallChannelDispatcher>.Instance,
            new CallLogRepository(_ctx, NullLogger<CallLogRepository>.Instance));

    private EscalationOrchestrator Orchestrator(ProviderAbsence absence)
    {
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
            [Sms(absence)],
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

    private async Task<Guid> SeedIncidentAsync()
    {
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.Open,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        _ctx.Incidents.Add(incident);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        return incident.Id;
    }

    /// <summary>An open incident mid-escalation whose next step pages one SMS-only responder.</summary>
    private async Task<Guid> SeedEscalatingIncidentAsync()
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
            SmsEnabled = true,
            VoiceEnabled = false,
            PushEnabled = false,
            Timezone = "UTC"
        });

        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        return incident.Id;
    }
}
