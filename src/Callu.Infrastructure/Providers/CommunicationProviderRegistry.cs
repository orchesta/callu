using System.Collections.Concurrent;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.Providers;

/// <summary>
/// Thread-safe registry for managing communication providers and capability routing.
/// Singleton lifetime — all mutable state is protected.
/// </summary>
public class CommunicationProviderRegistry : ICommunicationProviderRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<CommunicationSettingsOptions> _communicationOptions;
    private readonly ILogger<CommunicationProviderRegistry> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private readonly ConcurrentDictionary<string, Type> _providerTypes = new();

    private volatile ProviderSnapshot _snapshot = new();

    private int _lastLoadedCount = -1;

    public CommunicationProviderRegistry(
        IServiceProvider serviceProvider,
        IOptions<CommunicationSettingsOptions> communicationOptions,
        ILogger<CommunicationProviderRegistry> logger)
    {
        _serviceProvider = serviceProvider;
        _communicationOptions = communicationOptions;
        _logger = logger;
    }

    public void RegisterProviderType(string providerType, Type concreteType)
    {
        _providerTypes[providerType] = concreteType;
        _logger.LogInformation("Registered provider type mapping: {ProviderType} → {ConcreteType}",
            providerType, concreteType.Name);
    }

    public ICommunicationProvider? GetProvider(CommunicationCapability capability)
    {
        var snapshot = _snapshot;

        foreach (CommunicationCapability cap in Enum.GetValues<CommunicationCapability>())
        {
            if (cap == CommunicationCapability.None) continue;
            if (!capability.HasFlag(cap)) continue;

            if (snapshot.CapabilityProviders.TryGetValue(cap, out var providers) && providers.Count > 0)
                return providers[0];
        }

        return null;
    }

    public Task<ICommunicationProvider?> GetConfiguredProviderAsync(Guid providerId)
    {
        var snapshot = _snapshot;
        snapshot.ConfiguredProviders.TryGetValue(providerId, out var provider);
        return Task.FromResult(provider);
    }

    public IEnumerable<string> GetAvailableProviderTypes() => _providerTypes.Keys;

    public async Task ReloadProvidersAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            var newSnapshot = new ProviderSnapshot();

            using var scope = _serviceProvider.CreateScope();
            var communicationProviderRepository = scope.ServiceProvider.GetRequiredService<ICommunicationProviderRepository>();
            var capabilityMappingRepository = scope.ServiceProvider.GetRequiredService<ICapabilityProviderMappingRepository>();
            var sipTrunkSettingsRepository = scope.ServiceProvider.GetRequiredService<ISipTrunkSettingsRepository>();

            var providerEntities = await communicationProviderRepository.ListEnabledWithSipTrunkForRegistryReloadAsync(cancellationToken);
            var mappings = await capabilityMappingRepository.ListEnabledForRegistryReloadAsync(cancellationToken);
            var mappedCapabilities = mappings.Select(m => m.Capability).ToHashSet();

            var systemTrunkId = _communicationOptions.Value.SystemSipTrunkId;
            SipTrunkSettings? cachedSystemTrunk = null;

            foreach (var entity in providerEntities)
            {
                if (!_providerTypes.TryGetValue(entity.ProviderType, out var concreteType))
                {
                    _logger.LogWarning("Unknown provider type: {ProviderType} for provider '{Name}'",
                        entity.ProviderType, entity.Name);
                    continue;
                }

                IServiceScope? providerScope = null;
                try
                {
                    providerScope = _serviceProvider.CreateScope();
                    var provider = (ICommunicationProvider)providerScope.ServiceProvider.GetRequiredService(concreteType);

                    SipTrunkSettings? sipForInit = entity.SipTrunk;
                    if (sipForInit is null
                        && systemTrunkId is { } sysTrunkId
                        && entity.Capabilities.HasFlag(CommunicationCapability.VoiceCalls))
                    {
                        cachedSystemTrunk ??= await sipTrunkSettingsRepository.GetByIdIgnoringFiltersNoTrackingAsync(
                            sysTrunkId,
                            cancellationToken);
                        if (cachedSystemTrunk is not null &&
                            (!cachedSystemTrunk.IsEnabled || cachedSystemTrunk.IsDeleted))
                            cachedSystemTrunk = null;
                        sipForInit = cachedSystemTrunk;
                        if (sipForInit is not null)
                        {
                            _logger.LogDebug(
                                "Using system SIP trunk {TrunkId} for provider {Provider}",
                                sysTrunkId, entity.Name);
                        }
                    }

                    await provider.InitializeAsync(entity.ConfigJson ?? "{}", sipForInit);

                    newSnapshot.ConfiguredProviders[entity.Id] = provider;
                    newSnapshot.ProviderScopes[entity.Id] = providerScope;
                    providerScope = null;

                    foreach (CommunicationCapability cap in Enum.GetValues<CommunicationCapability>())
                    {
                        if (cap == CommunicationCapability.None) continue;
                        if (!provider.Capabilities.HasFlag(cap)) continue;
                        if (mappedCapabilities.Contains(cap)) continue;

                        if (!newSnapshot.CapabilityProviders.ContainsKey(cap))
                            newSnapshot.CapabilityProviders[cap] = [];
                        newSnapshot.CapabilityProviders[cap].Add(provider);
                    }

                    _logger.LogDebug("Loaded provider: {Name} ({Type})",
                        entity.Name, entity.ProviderType);
                }
                catch (Exception ex)
                {
                    providerScope?.Dispose();
                    _logger.LogError(ex, "Failed to initialize provider: {Name}", entity.Name);
                }
            }

            foreach (var mapping in mappings)
            {
                if (newSnapshot.ConfiguredProviders.TryGetValue(mapping.ProviderId, out var provider))
                {
                    if (!newSnapshot.CapabilityProviders.ContainsKey(mapping.Capability))
                        newSnapshot.CapabilityProviders[mapping.Capability] = [];
                    if (!newSnapshot.CapabilityProviders[mapping.Capability].Contains(provider))
                        newSnapshot.CapabilityProviders[mapping.Capability].Add(provider);
                }
            }

            var old = Interlocked.Exchange(ref _snapshot, newSnapshot);
            foreach (var s in old.ProviderScopes.Values) s.Dispose();

            var loadedCount = newSnapshot.ConfiguredProviders.Count;
            if (loadedCount != _lastLoadedCount)
            {
                _logger.LogInformation("Reloaded communication providers: {Count} active", loadedCount);
                _lastLoadedCount = loadedCount;
            }
            else
            {
                _logger.LogDebug("Reloaded communication providers: {Count} active (unchanged)", loadedCount);
            }
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private sealed class ProviderSnapshot
    {
        public Dictionary<Guid, ICommunicationProvider> ConfiguredProviders { get; } = new();
        public Dictionary<CommunicationCapability, List<ICommunicationProvider>> CapabilityProviders { get; } = new();
        public Dictionary<Guid, IServiceScope> ProviderScopes { get; } = new();
    }
}
