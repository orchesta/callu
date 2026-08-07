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
public class CommunicationProviderRegistry : ICommunicationProviderRegistry, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<CommunicationSettingsOptions> _communicationOptions;
    private readonly ILogger<CommunicationProviderRegistry> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private readonly ConcurrentDictionary<string, Type> _providerTypes = new();

    private volatile ProviderSnapshot _snapshot = new();

    private int _lastLoadedCount = -1;

    private readonly ConcurrentQueue<(DateTimeOffset RetiredAtUtc, IServiceScope Scope)> _retiredScopes = new();

    /// <summary>How long a retired provider DI scope is kept alive so an in-flight operation that captured
    /// the old provider can finish before the scope is torn down.</summary>
    internal static readonly TimeSpan ScopeDrainGrace = TimeSpan.FromSeconds(60);

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

    /// <summary>Why <see cref="GetProvider"/> came back empty-handed; a switched-off provider answers
    /// <see cref="ProviderAbsence.ConfiguredButUnavailable"/> so the page waits rather than being written off.</summary>
    public ProviderAbsence DescribeAbsence(CommunicationCapability capability)
    {
        var snapshot = _snapshot;

        if (!snapshot.IsLoaded)
            return ProviderAbsence.RegistryNotLoaded;

        foreach (CommunicationCapability cap in Enum.GetValues<CommunicationCapability>())
        {
            if (cap == CommunicationCapability.None) continue;
            if (!capability.HasFlag(cap)) continue;

            if (snapshot.ConfiguredCapabilities.Contains(cap))
                return ProviderAbsence.ConfiguredButUnavailable;
        }

        return ProviderAbsence.NotConfigured;
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
            // IsLoaded is the whole point of building it here: a snapshot that came out of a completed
            // reload is EVIDENCE — "there is no provider for this capability" is then an answer, not a
            // window. Only the initial field value (never assigned by this method) is unloaded.
            var newSnapshot = new ProviderSnapshot { IsLoaded = true };

            using var scope = _serviceProvider.CreateScope();
            var communicationProviderRepository = scope.ServiceProvider.GetRequiredService<ICommunicationProviderRepository>();
            var capabilityMappingRepository = scope.ServiceProvider.GetRequiredService<ICapabilityProviderMappingRepository>();
            var sipTrunkSettingsRepository = scope.ServiceProvider.GetRequiredService<ISipTrunkSettingsRepository>();

            // EVERY row, enabled or not: the list answers two different questions and only one of them
            // is about loading a provider (see below, and ListForRegistryReloadAsync).
            var providerEntities = await communicationProviderRepository.ListForRegistryReloadAsync(cancellationToken);
            var mappings = await capabilityMappingRepository.ListEnabledForRegistryReloadAsync(cancellationToken);

            var configuredProviderIds = new HashSet<Guid>();

            // The providers that came up, in priority order — indexed by capability only AFTER the whole
            // list is known, because a mapping cannot be allowed to displace them until we can see whether
            // its own target survived this reload (see routedCapabilities, below).
            var loadedProviders = new List<ICommunicationProvider>();

            var systemTrunkId = _communicationOptions.Value.SystemSipTrunkId;
            SipTrunkSettings? cachedSystemTrunk = null;

            foreach (var entity in providerEntities)
            {
                // "Configured" is the existence of the row, so this is recorded before anything can fail and
                // is never rolled back — a provider that cannot be used right now is not an absent one.
                configuredProviderIds.Add(entity.Id);
                foreach (CommunicationCapability cap in Enum.GetValues<CommunicationCapability>())
                {
                    if (cap == CommunicationCapability.None) continue;
                    if (entity.Capabilities.HasFlag(cap))
                        newSnapshot.ConfiguredCapabilities.Add(cap);
                }

                // ...and here is where the two questions part company. A disabled provider is configured,
                // and it is NOT loaded: GetProvider must not hand it to anybody, because the operator
                // switched it off on purpose.
                if (!entity.IsEnabled)
                {
                    _logger.LogDebug(
                        "Provider '{Name}' ({Type}) is configured but switched off; not loading it. Pages on its "
                        + "capabilities will WAIT rather than be written off as unconfigured.",
                        entity.Name, entity.ProviderType);
                    continue;
                }

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

                    loadedProviders.Add(provider);

                    _logger.LogDebug("Loaded provider: {Name} ({Type})",
                        entity.Name, entity.ProviderType);
                }
                catch (Exception ex)
                {
                    providerScope?.Dispose();
                    _logger.LogError(ex, "Failed to initialize provider: {Name}", entity.Name);
                }
            }

            // A mapping may only take a channel away from a provider if it can give it to one: a mapping onto
            // a target that did not come up is ignored, so the channel keeps its ordinary providers.
            var routedCapabilities = mappings
                .Where(m => newSnapshot.ConfiguredProviders.ContainsKey(m.ProviderId))
                .Select(m => m.Capability)
                .ToHashSet();

            foreach (var provider in loadedProviders)
            {
                foreach (CommunicationCapability cap in Enum.GetValues<CommunicationCapability>())
                {
                    if (cap == CommunicationCapability.None) continue;
                    if (!provider.Capabilities.HasFlag(cap)) continue;
                    if (routedCapabilities.Contains(cap)) continue;

                    if (!newSnapshot.CapabilityProviders.ContainsKey(cap))
                        newSnapshot.CapabilityProviders[cap] = [];
                    newSnapshot.CapabilityProviders[cap].Add(provider);
                }
            }

            foreach (var mapping in mappings)
            {
                // An explicit routing mapping onto a provider that EXISTS is a configured capability too —
                // again regardless of whether that provider is switched on, or loaded this time round.
                if (configuredProviderIds.Contains(mapping.ProviderId))
                    newSnapshot.ConfiguredCapabilities.Add(mapping.Capability);

                if (newSnapshot.ConfiguredProviders.TryGetValue(mapping.ProviderId, out var provider))
                {
                    if (!newSnapshot.CapabilityProviders.ContainsKey(mapping.Capability))
                        newSnapshot.CapabilityProviders[mapping.Capability] = [];
                    if (!newSnapshot.CapabilityProviders[mapping.Capability].Contains(provider))
                        newSnapshot.CapabilityProviders[mapping.Capability].Add(provider);
                }
            }

            var old = Interlocked.Exchange(ref _snapshot, newSnapshot);
            var nowUtc = DateTimeOffset.UtcNow;
            RetireScopes(old.ProviderScopes.Values, nowUtc);
            DrainRetiredScopes(nowUtc);

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

    internal void RetireScopes(IEnumerable<IServiceScope> scopes, DateTimeOffset nowUtc)
    {
        foreach (var scope in scopes)
            _retiredScopes.Enqueue((nowUtc, scope));
    }

    /// <summary>Disposes retired scopes whose grace window has elapsed and returns how many went.</summary>
    internal int DrainRetiredScopes(DateTimeOffset nowUtc)
    {
        var disposed = 0;
        while (_retiredScopes.TryPeek(out var entry) && nowUtc - entry.RetiredAtUtc >= ScopeDrainGrace)
        {
            if (!_retiredScopes.TryDequeue(out var due)) break;
            try
            {
                due.Scope.Dispose();
                disposed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose retired communication-provider scope");
            }
        }
        return disposed;
    }

    public void Dispose()
    {
        while (_retiredScopes.TryDequeue(out var entry))
        {
            try { entry.Scope.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to dispose retired provider scope on shutdown"); }
        }

        foreach (var scope in _snapshot.ProviderScopes.Values)
        {
            try { scope.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to dispose active provider scope on shutdown"); }
        }

        _reloadLock.Dispose();
    }

    private sealed class ProviderSnapshot
    {
        /// <summary>False only on the empty snapshot the registry is born with, before any reload completed.</summary>
        public bool IsLoaded { get; init; }

        /// <summary>The capabilities provider rows claim — the existence of the row, not its
        /// <c>IsEnabled</c> flag — as opposed to <see cref="CapabilityProviders"/>, which holds what came up.</summary>
        public HashSet<CommunicationCapability> ConfiguredCapabilities { get; } = [];

        public Dictionary<Guid, ICommunicationProvider> ConfiguredProviders { get; } = new();
        public Dictionary<CommunicationCapability, List<ICommunicationProvider>> CapabilityProviders { get; } = new();
        public Dictionary<Guid, IServiceScope> ProviderScopes { get; } = new();
    }
}
