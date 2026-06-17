using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// IncidentNote repository implementation
/// </summary>
public class IncidentNoteRepository(ApplicationDbContext context, ILogger<IncidentNoteRepository> logger)
    : Repository<IncidentNote>(context, logger), IIncidentNoteRepository
{
    public async Task<IEnumerable<IncidentNote>> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(n => n.IncidentId == incidentId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IncidentNote>> GetPinnedByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(n => n.IncidentId == incidentId && n.IsPinned)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
