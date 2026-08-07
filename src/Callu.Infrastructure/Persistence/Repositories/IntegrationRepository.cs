using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// Integration repository implementation
/// </summary>
public class IntegrationRepository(ApplicationDbContext context, ILogger<IntegrationRepository> logger)
    : Repository<Integration>(context, logger), IIntegrationRepository
{
    public async Task<Integration?> GetByWebhookTokenWithTemplateAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _context.Integrations
            .IgnoreQueryFilters()
            .Include(i => i.WebhookTemplate)
            .Include(i => i.Service)
            .FirstOrDefaultAsync(i => i.WebhookToken == token && !i.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<Integration>> GetByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i => i.ServiceId == serviceId)
            .OrderBy(i => i.Name)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);
    }
}
