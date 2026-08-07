using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Providers;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Callu.Tests;

/// <summary>A capability mapping may only take a channel away from a provider if it can give it to one.</summary>
public class CapabilityMappingCannotStrandAChannelTests : IDisposable
{
    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"capability-mapping-{Guid.NewGuid():N}").Options);

    private readonly ServiceProvider _services;
    private readonly CommunicationProviderRegistry _registry;

    public CapabilityMappingCannotStrandAChannelTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new PlacedCalls());
        services.AddSingleton(_ctx);
        services.AddScoped<ICommunicationProviderRepository>(_ =>
            new CommunicationProviderRepository(_ctx, NullLogger<CommunicationProviderRepository>.Instance));
        services.AddScoped<ICapabilityProviderMappingRepository>(_ =>
            new CapabilityProviderMappingRepository(_ctx, NullLogger<CapabilityProviderMappingRepository>.Instance));
        services.AddScoped<ISipTrunkSettingsRepository>(_ =>
            new SipTrunkSettingsRepository(_ctx, NullLogger<SipTrunkSettingsRepository>.Instance));
        services.AddScoped<StubVoiceProvider>();
        services.AddScoped<UnloadableVoiceProvider>();
        services.AddScoped<MappedVoiceProvider>();
        _services = services.BuildServiceProvider();

        _registry = new CommunicationProviderRegistry(
            _services,
            Options.Create(new CommunicationSettingsOptions()),
            NullLogger<CommunicationProviderRegistry>.Instance);

        _registry.RegisterProviderType(StubVoiceProvider.Type, typeof(StubVoiceProvider));
        _registry.RegisterProviderType(UnloadableVoiceProvider.Type, typeof(UnloadableVoiceProvider));
        _registry.RegisterProviderType(MappedVoiceProvider.Type, typeof(MappedVoiceProvider));
    }

    public void Dispose()
    {
        _registry.Dispose();
        _services.Dispose();
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── a mapping whose target is not usable must not displace anybody ────────────────────────────

    /// <summary>A mapping onto a switched-off provider cannot be honoured halfway, so the healthy provider keeps the channel.</summary>
    [Fact]
    public async Task AMappingOntoASwitchedOffProvider_DoesNotStripTheChannelFromTheHealthyOne()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true);
        var parked = await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, MappedVoiceProvider.Type, enabled: false);
        await SeedMappingAsync(CommunicationCapability.VoiceCalls, parked);

        await _registry.ReloadProvidersAsync();

        var provider = _registry.GetProvider(CommunicationCapability.VoiceCalls);

        Assert.NotNull(provider);
        Assert.Equal(StubVoiceProvider.Type, provider.ProviderType);
    }

    /// <summary>The stale row that outlives its target: deleting the mapped provider leaves the mapping behind.</summary>
    [Fact]
    public async Task AMappingOntoADeletedProvider_DoesNotStripTheChannelFromTheHealthyOne()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true);
        var removed = await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, MappedVoiceProvider.Type, enabled: true);
        await SeedMappingAsync(CommunicationCapability.VoiceCalls, removed);
        await SetProviderAsync(removed, p => p.IsDeleted = true);

        await _registry.ReloadProvidersAsync();

        var provider = _registry.GetProvider(CommunicationCapability.VoiceCalls);

        Assert.NotNull(provider);
        Assert.Equal(StubVoiceProvider.Type, provider.ProviderType);
    }

    /// <summary>The transient case: the mapped provider failed to initialise, so the fallback holds until it returns.</summary>
    [Fact]
    public async Task AMappingOntoAProviderThatFailedToInitialise_DoesNotStripTheChannelFromTheHealthyOne()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true);
        var broken = await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, UnloadableVoiceProvider.Type, enabled: true);
        await SeedMappingAsync(CommunicationCapability.VoiceCalls, broken);

        await _registry.ReloadProvidersAsync();

        var provider = _registry.GetProvider(CommunicationCapability.VoiceCalls);

        Assert.NotNull(provider);
        Assert.Equal(StubVoiceProvider.Type, provider.ProviderType);
    }

    /// <summary>A mapping onto a provider type this host cannot construct must not strand the channel either.</summary>
    [Fact]
    public async Task AMappingOntoAnUnknownProviderType_DoesNotStripTheChannelFromTheHealthyOne()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true);
        var alien = await SeedProviderAsync(CommunicationCapability.VoiceCalls, "not-deployed-here", enabled: true);
        await SeedMappingAsync(CommunicationCapability.VoiceCalls, alien);

        await _registry.ReloadProvidersAsync();

        var provider = _registry.GetProvider(CommunicationCapability.VoiceCalls);

        Assert.NotNull(provider);
        Assert.Equal(StubVoiceProvider.Type, provider.ProviderType);
    }

    /// <summary>A stray mapping does not spread: a voice mapping leaves the same provider's SMS untouched.</summary>
    [Fact]
    public async Task AMappingOnVoice_DoesNotDisturbTheSameProvidersSms()
    {
        await SeedProviderAsync(
            CommunicationCapability.VoiceCalls | CommunicationCapability.Sms,
            StubVoiceProvider.Type, enabled: true);
        var parked = await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, MappedVoiceProvider.Type, enabled: false);
        await SeedMappingAsync(CommunicationCapability.VoiceCalls, parked);

        await _registry.ReloadProvidersAsync();

        var sms = _registry.GetProvider(CommunicationCapability.Sms);
        Assert.NotNull(sms);
        Assert.Equal(StubVoiceProvider.Type, sms.ProviderType);
    }

    // ── ...and a mapping that CAN be honoured still is ────────────────────────────────────────────

    /// <summary>A mapping onto a loaded provider still wins over the provider it displaces.</summary>
    [Fact]
    public async Task AMappingOntoALoadedProvider_StillWins_OverTheProviderItDisplaces()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true);
        var chosen = await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, MappedVoiceProvider.Type, enabled: true);
        await SeedMappingAsync(CommunicationCapability.VoiceCalls, chosen);

        await _registry.ReloadProvidersAsync();

        var provider = _registry.GetProvider(CommunicationCapability.VoiceCalls);

        Assert.NotNull(provider);
        Assert.Equal(MappedVoiceProvider.Type, provider.ProviderType);
    }

    /// <summary>The fallback is a reprieve for one reload tick, not a demotion: the routing returns with its target.</summary>
    [Fact]
    public async Task WhenTheMappedProviderComesBack_TheRoutingIsHonouredAgain()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true);
        var chosen = await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, MappedVoiceProvider.Type, enabled: false);
        await SeedMappingAsync(CommunicationCapability.VoiceCalls, chosen);

        await _registry.ReloadProvidersAsync();
        Assert.Equal(StubVoiceProvider.Type, _registry.GetProvider(CommunicationCapability.VoiceCalls)!.ProviderType);

        await SetProviderAsync(chosen, p => p.IsEnabled = true);
        await _registry.ReloadProvidersAsync();

        Assert.Equal(MappedVoiceProvider.Type, _registry.GetProvider(CommunicationCapability.VoiceCalls)!.ProviderType);
    }

    /// <summary>A switched-off mapping row routes nothing and displaces nothing, unlike a switched-off provider row.</summary>
    [Fact]
    public async Task ASwitchedOffMappingRow_RoutesNothingAndDisplacesNothing()
    {
        // Distinct priorities on purpose: with the mapping ignored the fallback is the priority order,
        // and two providers left at the same priority would only pin whichever the tiebreak happened to pick.
        await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true, priority: 0);
        var chosen = await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, MappedVoiceProvider.Type, enabled: true, priority: 1);
        await SeedMappingAsync(CommunicationCapability.VoiceCalls, chosen, enabled: false);

        await _registry.ReloadProvidersAsync();

        var provider = _registry.GetProvider(CommunicationCapability.VoiceCalls);

        Assert.NotNull(provider);
        Assert.Equal(StubVoiceProvider.Type, provider.ProviderType);
    }

    /// <summary>With the mapping ignored the channel is not merely configured but usable, so a page goes out now.</summary>
    [Fact]
    public async Task WithTheMappingIgnored_TheChannelIsNotMerelyConfigured_ItIsUsable()
    {
        await SeedProviderAsync(CommunicationCapability.VoiceCalls, StubVoiceProvider.Type, enabled: true);
        var parked = await SeedProviderAsync(
            CommunicationCapability.VoiceCalls, MappedVoiceProvider.Type, enabled: false);
        await SeedMappingAsync(CommunicationCapability.VoiceCalls, parked);

        await _registry.ReloadProvidersAsync();

        Assert.NotNull(_registry.GetProvider(CommunicationCapability.VoiceCalls));
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedProviderAsync(
        CommunicationCapability capabilities, string providerType, bool enabled, int priority = 0)
    {
        var provider = new CommunicationProvider
        {
            Id = Guid.NewGuid(),
            Name = $"{providerType} ({capabilities})",
            ProviderType = providerType,
            Capabilities = capabilities,
            ConfigJson = "{}",
            IsEnabled = enabled,
            Priority = priority
        };

        _ctx.Add(provider);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        return provider.Id;
    }

    /// <summary>A row exactly as a future "route this capability through that provider" screen would write one.</summary>
    private async Task SeedMappingAsync(
        CommunicationCapability capability, Guid providerId, bool enabled = true)
    {
        _ctx.Add(new CapabilityProviderMapping
        {
            Id = Guid.NewGuid(),
            Capability = capability,
            ProviderId = providerId,
            Priority = 0,
            IsEnabled = enabled
        });

        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();
    }

    private async Task SetProviderAsync(Guid providerId, Action<CommunicationProvider> change)
    {
        var provider = await _ctx.Set<CommunicationProvider>()
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == providerId);

        change(provider);

        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();
    }
}

/// <summary>
/// The provider a mapping points AT — distinct from <see cref="StubVoiceProvider"/> only so the tests
/// can tell which one the registry actually handed out.
/// </summary>
internal sealed class MappedVoiceProvider : ICommunicationProvider
{
    public const string Type = "mapped-voice";

    public string ProviderType => Type;

    public CommunicationCapability Capabilities => CommunicationCapability.VoiceCalls;

    public Task InitializeAsync(string configJson, SipTrunkSettings? sipTrunk) => Task.CompletedTask;

    public Task<(bool Success, string Message)> TestConnectionAsync() => Task.FromResult((true, "ok"));

    public Task<CallResult> MakeCallAsync(MakeCallRequest request) =>
        Task.FromResult(new CallResult { Success = true, CallId = Guid.NewGuid().ToString() });

    public Task HangupCallAsync(string callId) => Task.CompletedTask;

    public Task<SmsResult> SendSmsAsync(SendSmsRequest request) => throw new NotSupportedException();

    public Task<SmsResult> SendWhatsAppAsync(SendSmsRequest request) => throw new NotSupportedException();

    public Task<ConferenceResult> CreateConferenceAsync(CreateConferenceRequest request) =>
        throw new NotSupportedException();

    public Task AddParticipantAsync(string conferenceId, string destination) => Task.CompletedTask;

    public Task EndConferenceAsync(string conferenceId) => Task.CompletedTask;

    public Task<byte[]> SynthesizeSpeechAsync(TTSRequest request) => Task.FromResult(Array.Empty<byte>());

    public Task<string> RecognizeSpeechAsync(byte[] audio, string? language) => Task.FromResult(string.Empty);
}
