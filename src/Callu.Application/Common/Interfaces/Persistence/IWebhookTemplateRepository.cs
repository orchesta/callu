using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// WebhookTemplate-specific repository interface
/// </summary>
public interface IWebhookTemplateRepository : IRepository<WebhookTemplate>
{
    Task<IEnumerable<WebhookTemplate>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<WebhookTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}
