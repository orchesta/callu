using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class WebhookTemplateRepository(ApplicationDbContext context, ILogger<WebhookTemplateRepository> logger)
    : Repository<WebhookTemplate>(context, logger), IWebhookTemplateRepository
{
    public async Task<IEnumerable<WebhookTemplate>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => !t.IsDeleted && t.IsActive)
            .OrderByDescending(t => t.IsBuiltIn)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<WebhookTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(t => EF.Functions.ILike(t.Name, name) && !t.IsDeleted, cancellationToken);
    }
}
