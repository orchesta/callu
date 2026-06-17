using Callu.Domain.Enums;

namespace Callu.Application.Providers;

/// <summary>
/// Registry for managing communication providers and capability routing.
/// </summary>
public interface ICommunicationProviderRegistry
{
    ICommunicationProvider? GetProvider(CommunicationCapability capability);
    Task<ICommunicationProvider?> GetConfiguredProviderAsync(Guid providerId);
    Task ReloadProvidersAsync(CancellationToken cancellationToken = default);
    IEnumerable<string> GetAvailableProviderTypes();
}
