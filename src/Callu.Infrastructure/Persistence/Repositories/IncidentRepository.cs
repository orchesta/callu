using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// Incident repository implementation
/// </summary>
public class IncidentRepository(ApplicationDbContext context, ILogger<IncidentRepository> logger)
    : Repository<Incident>(context, logger), IIncidentRepository
{
    public async Task<IEnumerable<Incident>> GetActiveIncidentsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i => i.Status != IncidentStatus.Closed && i.Status != IncidentStatus.Resolved)
            .OrderByDescending(i => i.Severity)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Incident>> GetByStatusAsync(IncidentStatus status, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i => i.Status == status)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Incident>> GetBySeverityAsync(IncidentSeverity severity, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i => i.Severity == severity)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Incident>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i => i.TeamId == teamId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Incident>> GetByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i => i.ServiceId == serviceId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Incident?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(i => i.Service)
            .Include(i => i.Team)
            .Include(i => i.Notes.OrderByDescending(n => n.CreatedAt))
            .Include(i => i.TimelineEvents.OrderByDescending(t => t.CreatedAt))
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<Incident?> GetWithServiceAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbSet
            .AsNoTracking()
            .Include(i => i.Service)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .CountAsync(i => i.Status != IncidentStatus.Closed && i.Status != IncidentStatus.Resolved, cancellationToken);
    }
}
