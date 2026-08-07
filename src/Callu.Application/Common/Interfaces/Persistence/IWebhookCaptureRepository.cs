using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// WebhookCapture-specific repository interface
/// </summary>
public interface IWebhookCaptureRepository : IRepository<WebhookCapture>
{
    Task<IEnumerable<WebhookCapture>> GetByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<int> GetCountByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);
}
