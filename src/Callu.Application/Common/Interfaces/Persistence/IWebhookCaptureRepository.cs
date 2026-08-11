using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// WebhookCapture-specific repository interface
/// </summary>
public interface IWebhookCaptureRepository : IRepository<WebhookCapture>
{
    Task<IEnumerable<WebhookCapture>> GetByServiceAsync(Guid serviceId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> GetCountByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<WebhookCapture>> GetByIntegrationAsync(Guid integrationId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<int> GetCountByIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, int>> GetCountsByIntegrationAsync(IReadOnlyCollection<Guid> integrationIds, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes one capture; false when it does not exist.</summary>
    Task<bool> HardDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes every capture in one endpoint's scope, in bounded batches.</summary>
    Task<int> HardDeleteScopeAsync(Guid? serviceId, Guid? integrationId, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes the oldest rows of one endpoint's scope so a pending insert lands within the cap.</summary>
    Task<int> TrimScopeForPendingInsertAsync(Guid? serviceId, Guid? integrationId, int cap, int maxRows, CancellationToken cancellationToken = default);
}
