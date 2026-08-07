using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// CommunicationProvider-specific repository interface
/// </summary>
public interface ICommunicationProviderRepository : IRepository<CommunicationProvider>
{
    Task<IEnumerable<CommunicationProvider>> GetEnabledAsync(CancellationToken cancellationToken = default);
    Task<CommunicationProvider?> GetWithSipTrunkAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Highest-priority enabled row.</summary>
    Task<CommunicationProvider?> GetHighestPriorityEnabledNoTrackingAsync(CancellationToken cancellationToken = default);

    /// <summary>Registry reload: EVERY provider row that exists — enabled or not — with its SIP trunk.
    /// Soft-deleted rows are excluded; a removed provider genuinely leaves its channel with none.</summary>
    Task<IReadOnlyList<CommunicationProvider>> ListForRegistryReloadAsync(CancellationToken cancellationToken = default);
}
