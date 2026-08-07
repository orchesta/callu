using Callu.Domain.Enums;

namespace Callu.Application.Providers;

/// <summary>Why <see cref="ICommunicationProviderRegistry.GetProvider"/> handed back nothing — three
/// distinct facts, with opposite consequences for whether a page waits or gives up.</summary>
public enum ProviderAbsence
{
    /// <summary>No load has completed yet, so nothing can be concluded and the page WAITS. Deliberately the
    /// default(enum) value, so an implementation that forgets to answer falls on the side that keeps paging.</summary>
    RegistryNotLoaded = 0,

    /// <summary>A load completed and a row claiming this capability exists, but no usable provider came out of
    /// it — init failed, an operator switched it off, or the reload has not reached it. The page WAITS, bounded.</summary>
    ConfiguredButUnavailable = 1,

    /// <summary>A load completed and found no provider row for this capability at all, so a page on this channel
    /// would never be sent. The page STOPS, and says so.</summary>
    NotConfigured = 2
}

/// <summary>
/// Registry for managing communication providers and capability routing.
/// </summary>
public interface ICommunicationProviderRegistry
{
    ICommunicationProvider? GetProvider(CommunicationCapability capability);

    /// <summary>Why <see cref="GetProvider"/> would return null for <paramref name="capability"/>, answered from
    /// the same atomically-swapped snapshot so it cannot disagree with the call beside it.</summary>
    ProviderAbsence DescribeAbsence(CommunicationCapability capability);

    Task<ICommunicationProvider?> GetConfiguredProviderAsync(Guid providerId);
    Task ReloadProvidersAsync(CancellationToken cancellationToken = default);
    IEnumerable<string> GetAvailableProviderTypes();
}
