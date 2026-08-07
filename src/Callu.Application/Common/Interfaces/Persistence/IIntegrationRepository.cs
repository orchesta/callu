using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Integration-specific repository interface
/// </summary>
public interface IIntegrationRepository : IRepository<Integration>
{
    /// <summary>Webhook ingress: match by token, with the parsing template and owning service loaded.</summary>
    Task<Integration?> GetByWebhookTokenWithTemplateAsync(string token, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Integration>> GetByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);
}
