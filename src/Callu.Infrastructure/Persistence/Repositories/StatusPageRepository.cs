using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class StatusPageRepository(ApplicationDbContext context, ILogger<StatusPageRepository> logger)
    : Repository<StatusPage>(context, logger), IStatusPageRepository
{
    public async Task<StatusPage?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => p.Slug == slug && !p.IsDeleted, cancellationToken);
    }

    /// <summary>Public-safe: bypasses tenant filter so anonymous slug lookups work across orgs.</summary>
    public async Task<StatusPage?> GetBySlugPublicAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .Where(p => p.Slug == slug && !p.IsDeleted && p.IsPublic)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Public-safe detail with components + incidents.</summary>
    public async Task<StatusPage?> GetDetailBySlugPublicAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .Include(p => p.Components.Where(c => !c.IsDeleted).OrderBy(c => c.DisplayOrder))
            .Include(p => p.Incidents.Where(i => !i.IsDeleted).OrderByDescending(i => i.CreatedAt))
                .ThenInclude(i => i.Updates.OrderByDescending(u => u.CreatedAt))
            .Where(p => p.Slug == slug && !p.IsDeleted && p.IsPublic)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<StatusPage?> GetDetailByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Components.Where(c => !c.IsDeleted).OrderBy(c => c.DisplayOrder))
            .Include(p => p.Incidents.Where(i => !i.IsDeleted).OrderByDescending(i => i.CreatedAt))
                .ThenInclude(i => i.Updates.OrderByDescending(u => u.CreatedAt))
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    public async Task<StatusPage?> GetDetailBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Components.Where(c => !c.IsDeleted).OrderBy(c => c.DisplayOrder))
            .Include(p => p.Incidents.Where(i => !i.IsDeleted).OrderByDescending(i => i.CreatedAt))
                .ThenInclude(i => i.Updates.OrderByDescending(u => u.CreatedAt))
            .FirstOrDefaultAsync(p => p.Slug == slug && !p.IsDeleted, cancellationToken);
    }
}
