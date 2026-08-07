using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// Service repository implementation
/// </summary>
public class ServiceRepository(ApplicationDbContext context, ILogger<ServiceRepository> logger)
    : Repository<Service>(context, logger), IServiceRepository
{
    public async Task<Service?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(s => EF.Functions.ILike(s.Name, name), cancellationToken);
    }

    public async Task<IEnumerable<Service>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.TeamId == teamId)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Service>> GetByStatusAsync(ServiceStatus status, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.Status == status)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Service>> GetPublicServicesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.IsPublic)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Service?> GetByWebhookTokenWithTemplateAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _context.Services
            .IgnoreQueryFilters()
            .Include(s => s.WebhookTemplate)
            .FirstOrDefaultAsync(s => s.WebhookToken == token && !s.IsDeleted, cancellationToken);
    }
}
