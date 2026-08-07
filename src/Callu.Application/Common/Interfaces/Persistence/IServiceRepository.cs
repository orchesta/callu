using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Service-specific repository interface
/// </summary>
public interface IServiceRepository : IRepository<Service>
{
    Task<Service?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IEnumerable<Service>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Service>> GetByStatusAsync(ServiceStatus status, CancellationToken cancellationToken = default);
    Task<IEnumerable<Service>> GetPublicServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Webhook ingress: match by token across tenants (ignores org query filter).</summary>
    Task<Service?> GetByWebhookTokenWithTemplateAsync(string token, CancellationToken cancellationToken = default);
}
