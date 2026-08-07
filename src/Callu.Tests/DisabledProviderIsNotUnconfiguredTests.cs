using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Notifications;
using Callu.Shared.Models.Schedules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Drives the real provider registry over the four ways a capability can have no usable provider.</summary>
public class DisabledProviderIsNotUnconfiguredTests : IDisposable
{
    private const string OnCallResponder = "responder-oncall";
    private const string Phone = "+905551112233";

    private static readonly Guid ScheduleId = Guid.NewGuid();

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"disabled-provider-{Guid.NewGuid():N}").Options);

    private readonly PlacedCalls _placedCalls = new();
    private readonly ServiceProvider _services;
    private readonly CommunicationProviderRegistry _registry;
    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public DisabledProviderIsNotUnconfiguredTests()
    {
        _contacts.GetContactByIdAsync(OnCallResponder, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(OnCallResponder, "On-call", Phone, "oncall@example.io"));

        var services = new ServiceCollection();
        services.AddSingleton(_placedCalls);
        services.AddSingleton(_ctx);
        services.AddScoped<ICommunicationProviderRepository>(_ =>
            new CommunicationProviderRepository(_ctx, NullLogger<CommunicationProviderRepository>.Instance));
        services.AddScoped<ICapabilityProviderMappingRepository>(_ =>
            new CapabilityProviderMappingRepository(_ctx, NullLogger<CapabilityProviderMappingRepository>.Instance));
        services.AddScoped<ISipTrunkSettingsRepository>(_ =>
            new SipTrunkSettingsRepository(_ctx, NullLogger<SipTrunkSettingsRepository>.Instance));
        services.AddScoped<StubVoiceProvider>();
        services.AddScoped<UnloadableVoiceProvider>();
        _services = services.BuildServiceProvider();

        _registry = new CommunicationProviderRegistry(
            _services,
            Options.Create(new CommunicationSettingsOptions()),
            NullLogger<CommunicationProviderRegistry>.Instance);

        _registry.RegisterProviderType(StubVoiceProvider.Type, typeof(StubVoiceProvider));
        _registry.RegisterProviderType(UnloadableVoiceProvider.Type, typeof(UnloadableVoiceProvider));
    }

    public void Dispose()
    {
        _registry.Dispose();
        _services.Dispose();
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── what the registry says a channel IS, asked of the registry itself ─────────────────────────

    /// <summary>A switched-off provider row is configured but unavailable: not handed out, not reported as never configured.</summary>
    [Fact]
    public async Task AProviderRowThatIsSwitchedOff_IsConfiguredButUnavailable_NotUnconfigured()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: false);

        await _registry.ReloadProvidersAsync();

        Assert.Null(_registry.GetProvider(CommunicationCapability.VoiceCalls));

        Assert.Equal(
            ProviderAbsence.ConfiguredButUnavailable,
            _registry.DescribeAbsence(CommunicationCapability.VoiceCalls));
    }

    /// <summary>A channel with no provider row at all is genuinely unconfigured, and a page on it stops at once.</summary>
    [Fact]
    public async Task AChannelWithNoProviderRowAtAll_IsNotConfigured()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true);

        await _registry.ReloadProvidersAsync();

        Assert.NotNull(_registry.GetProvider(CommunicationCapability.VoiceCalls));
        Assert.Equal(ProviderAbsence.NotConfigured, _registry.DescribeAbsence(CommunicationCapability.Sms));
    }

    /// <summary>An empty providers table: every capability is unconfigured, and nothing pretends otherwise.</summary>
    [Fact]
    public async Task AnInstallationWithNoProvidersAtAll_SaysSo_ForEveryChannel()
    {
        await _registry.ReloadProvidersAsync();

        Assert.Equal(ProviderAbsence.NotConfigured, _registry.DescribeAbsence(CommunicationCapability.VoiceCalls));
        Assert.Equal(ProviderAbsence.NotConfigured, _registry.DescribeAbsence(CommunicationCapability.Sms));
    }

    /// <summary>
    /// The Turn-10 win: a provider that IS enabled and whose <c>InitializeAsync</c> threw on this reload
    /// is configured and broken — a fault that usually clears on the next tick. The page waits.
    /// </summary>
    [Fact]
    public async Task AProviderWhoseInitializeThrew_IsConfiguredButUnavailable()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, UnloadableVoiceProvider.Type, enabled: true);

        await _registry.ReloadProvidersAsync();

        Assert.Null(_registry.GetProvider(CommunicationCapability.VoiceCalls));
        Assert.Equal(
            ProviderAbsence.ConfiguredButUnavailable,
            _registry.DescribeAbsence(CommunicationCapability.VoiceCalls));
    }

    /// <summary>A soft-deleted provider row is gone rather than parked, drawn by the soft-delete filter not by IsEnabled.</summary>
    [Fact]
    public async Task ASoftDeletedProviderRow_IsGone_NotMerelySwitchedOff()
    {
        var id = await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true);
        await SetProviderAsync(id, p => p.IsDeleted = true);

        await _registry.ReloadProvidersAsync();

        Assert.Equal(ProviderAbsence.NotConfigured, _registry.DescribeAbsence(CommunicationCapability.VoiceCalls));
    }

    /// <summary>Configured is per capability, so a switched-off SMS gateway does not make voice look configured.</summary>
    [Fact]
    public async Task ASwitchedOffSmsGateway_DoesNotMakeVoiceLookConfigured()
    {
        await SeedProviderAsync(CommunicationCapability.Sms, StubVoiceProvider.Type, enabled: false);

        await _registry.ReloadProvidersAsync();

        Assert.Equal(ProviderAbsence.ConfiguredButUnavailable, _registry.DescribeAbsence(CommunicationCapability.Sms));
        Assert.Equal(ProviderAbsence.NotConfigured, _registry.DescribeAbsence(CommunicationCapability.VoiceCalls));
    }

    /// <summary>Before the first reload nothing has been asked, and the enum's default errs towards keeping a page alive.</summary>
    [Fact]
    public void ARegistryThatHasNotLoadedYet_SaysSo_RatherThanClaimingNothingIsConfigured()
    {
        Assert.Equal(ProviderAbsence.RegistryNotLoaded, _registry.DescribeAbsence(CommunicationCapability.VoiceCalls));
    }

    // ── and what that means for a page, through the real dispatcher ───────────────────────────────

    /// <summary>
    /// The page an operator's click used to kill. It must be POSTPONED — <c>Pending</c>, a sixty-second
    /// deadline, retry budget untouched, inside the sweep's claim window — and not written off.
    /// </summary>
    [Fact]
    public async Task AVoicePage_WhoseProviderIsSwitchedOff_Waits_RatherThanBeingWrittenOff()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: false);
        await _registry.ReloadProvidersAsync();

        var page = VoicePage();
        await Voice().SendAsync(page, null, Phone, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Pending, page.DeliveryStatus);
        Assert.NotEqual(NotificationDeliveryStatus.Skipped, page.DeliveryStatus);
        Assert.NotNull(page.NextRetryAt);
        Assert.Equal(0, page.RetryCount);

        Assert.True(page.RetrySweepWillTakeIt, "the sweep must own this page — nothing else will send it");
        Assert.True(page.PageIsOnItsWay);
        Assert.True(page.CountsAsReached);
        Assert.Empty(_placedCalls.Destinations);
    }

    /// <summary>The waiting page is actually dialled once the provider is switched back on.</summary>
    [Fact]
    public async Task WhenTheProviderIsSwitchedBackOn_TheWaitingPage_IsActuallyDialled()
    {
        var incidentId = await SeedOpenIncidentAsync();
        var providerId = await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: false);
        await _registry.ReloadProvidersAsync();

        var page = VoicePage(incidentId);
        await Voice().SendAsync(page, null, Phone, Payload(incidentId), null);
        Assert.Equal(NotificationDeliveryStatus.Pending, page.DeliveryStatus);

        // The operator flips it back on; the registry reload picks it up on its next tick.
        await SetProviderAsync(providerId, p => p.IsEnabled = true);
        await _registry.ReloadProvidersAsync();

        // ...and the retry sweep re-drives exactly the row it deferred.
        await Voice().RetryAsync(page, baseUrl: null);

        Assert.Equal(NotificationDeliveryStatus.Delivered, page.DeliveryStatus);
        Assert.Equal(Phone, Assert.Single(_placedCalls.Destinations));
    }

    /// <summary>A provider left switched off writes the page off, but only once the wall-clock window is spent.</summary>
    [Fact]
    public async Task AProviderLeftSwitchedOff_WritesThePageOff_ButOnlyAfterTheWindow()
    {
        var incidentId = await SeedOpenIncidentAsync();
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: false);
        await _registry.ReloadProvidersAsync();

        var page = VoicePage(incidentId);
        page.CreatedAt =
            DateTime.UtcNow - PhoneChannelDispatcher.ProviderUnavailableWindow - TimeSpan.FromMinutes(1);

        await Voice().RetryAsync(page, baseUrl: null);

        Assert.Equal(NotificationDeliveryStatus.Skipped, page.DeliveryStatus);
        Assert.Null(page.NextRetryAt);
        Assert.False(page.PageIsOnItsWay);
    }

    // ── the ladder, over the real orchestrator ────────────────────────────────────────────────────

    /// <summary>Five Quartz ticks with the voice provider toggled off leave the three-step policy alive on step 1.</summary>
    [Fact]
    public async Task SwitchingAProviderOff_DoesNotExhaustAThreeStepPolicy_InThirtySeconds()
    {
        var incidentId = await SeedIncidentWithThreeScheduleStepsAsync();
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: false);
        await _registry.ReloadProvidersAsync();

        var orchestrator = Orchestrator();
        for (var tick = 0; tick < 5; tick++)
            await orchestrator.ProcessPendingEscalationsAsync();

        var incident = await _ctx.Incidents.AsNoTracking().SingleAsync(i => i.Id == incidentId);

        Assert.True(incident.IsEscalationActive,
            "the policy ran to exhaustion because a provider was switched off for a minute: the page was written "
            + "off as 'no provider was ever configured', every step believed it had nothing in flight, and the "
            + "ladder collapsed in thirty seconds without dialling once");

        var steps = await _ctx.Set<EscalationStep>().AsNoTracking().OrderBy(s => s.Level).ToListAsync();
        Assert.Equal(steps[0].Id, incident.CurrentEscalationStepId);

        // One page, to one person — and it is alive: the sweep owns it and will dial it the moment the
        // provider comes back.
        var page = Assert.Single(await _ctx.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.VoiceCall)
            .ToListAsync());

        Assert.Equal(NotificationDeliveryStatus.Pending, page.DeliveryStatus);
        Assert.True(page.RetrySweepWillTakeIt);
        Assert.Empty(_placedCalls.Destinations);
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────

    private static NotificationPayload Payload(Guid? incidentId = null) => new()
    {
        IncidentId = incidentId ?? Guid.NewGuid(),
        Title = "Checkout failing",
        Severity = "Critical",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1
    };

    private static Notification VoicePage(Guid? incidentId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = OnCallResponder,
        Type = NotificationType.VoiceCall,
        Title = "Escalation step 1",
        IncidentId = incidentId,
        DeliveryStatus = NotificationDeliveryStatus.Sending,
        CreatedAt = DateTime.UtcNow
    };

    private VoiceCallChannelDispatcher Voice() =>
        new VoiceCallChannelDispatcher(
            _contacts,
            _registry,
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<VoiceCallChannelDispatcher>.Instance,
            new CallLogRepository(_ctx, NullLogger<CallLogRepository>.Instance));

    private EscalationOrchestrator Orchestrator()
    {
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
            [Voice()],
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

    /// <summary>A provider row exactly as the Communications settings screen writes one.</summary>
    private async Task<Guid> SeedProviderAsync(
        CommunicationCapability capabilities, string providerType, bool enabled)
    {
        var provider = new CommunicationProvider
        {
            Id = Guid.NewGuid(),
            Name = $"{providerType} ({capabilities})",
            ProviderType = providerType,
            Capabilities = capabilities,
            ConfigJson = "{}",
            IsEnabled = enabled,
            Priority = 0
        };

        _ctx.Add(provider);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        return provider.Id;
    }

    /// <summary>The operator flipping a switch on the provider row — one click, one column.</summary>
    private async Task SetProviderAsync(Guid providerId, Action<CommunicationProvider> change)
    {
        var provider = await _ctx.Set<CommunicationProvider>()
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == providerId);

        change(provider);

        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();
    }

    private async Task<Guid> SeedOpenIncidentAsync()
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

    /// <summary>The ordinary configuration: three schedule-targeted steps, 0/2/3-minute delays.</summary>
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

/// <summary>Where the phone calls land, since the registry rebuilds provider instances on every reload.</summary>
internal sealed class PlacedCalls
{
    public List<string> Destinations { get; } = [];
}

/// <summary>A provider that works. Resolved by concrete type out of the registry's own DI scope.</summary>
internal sealed class StubVoiceProvider(PlacedCalls placedCalls) : ICommunicationProvider
{
    public const string Type = "stub-voice";

    public string ProviderType => Type;

    public CommunicationCapability Capabilities =>
        CommunicationCapability.VoiceCalls | CommunicationCapability.Sms;

    public Task InitializeAsync(string configJson, SipTrunkSettings? sipTrunk) => Task.CompletedTask;

    public Task<(bool Success, string Message)> TestConnectionAsync() => Task.FromResult((true, "ok"));

    public Task<CallResult> MakeCallAsync(MakeCallRequest request)
    {
        placedCalls.Destinations.Add(request.Destination);
        return Task.FromResult(new CallResult { Success = true, CallId = Guid.NewGuid().ToString() });
    }

    public Task HangupCallAsync(string callId) => Task.CompletedTask;

    public Task<SmsResult> SendSmsAsync(SendSmsRequest request) =>
        Task.FromResult(new SmsResult { Success = true });

    public Task<SmsResult> SendWhatsAppAsync(SendSmsRequest request) =>
        Task.FromResult(new SmsResult { Success = true });

    public Task<ConferenceResult> CreateConferenceAsync(CreateConferenceRequest request) =>
        throw new NotSupportedException();

    public Task AddParticipantAsync(string conferenceId, string destination) => Task.CompletedTask;

    public Task EndConferenceAsync(string conferenceId) => Task.CompletedTask;

    public Task<byte[]> SynthesizeSpeechAsync(TTSRequest request) => Task.FromResult(Array.Empty<byte>());

    public Task<string> RecognizeSpeechAsync(byte[] audio, string? language) => Task.FromResult(string.Empty);
}

/// <summary>A configured, enabled provider whose InitializeAsync throws: out of the snapshot, still configured.</summary>
internal sealed class UnloadableVoiceProvider : ICommunicationProvider
{
    public const string Type = "unloadable-voice";

    public string ProviderType => Type;

    public CommunicationCapability Capabilities => CommunicationCapability.VoiceCalls;

    public Task InitializeAsync(string configJson, SipTrunkSettings? sipTrunk) =>
        throw new InvalidOperationException("the SIP trunk could not be fetched");

    public Task<(bool Success, string Message)> TestConnectionAsync() => Task.FromResult((false, "down"));

    public Task<CallResult> MakeCallAsync(MakeCallRequest request) => throw new NotSupportedException();

    public Task HangupCallAsync(string callId) => throw new NotSupportedException();

    public Task<SmsResult> SendSmsAsync(SendSmsRequest request) => throw new NotSupportedException();

    public Task<SmsResult> SendWhatsAppAsync(SendSmsRequest request) => throw new NotSupportedException();

    public Task<ConferenceResult> CreateConferenceAsync(CreateConferenceRequest request) =>
        throw new NotSupportedException();

    public Task AddParticipantAsync(string conferenceId, string destination) => throw new NotSupportedException();

    public Task EndConferenceAsync(string conferenceId) => throw new NotSupportedException();

    public Task<byte[]> SynthesizeSpeechAsync(TTSRequest request) => throw new NotSupportedException();

    public Task<string> RecognizeSpeechAsync(byte[] audio, string? language) => throw new NotSupportedException();
}
