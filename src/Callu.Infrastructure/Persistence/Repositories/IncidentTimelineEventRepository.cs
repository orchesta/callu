using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// IncidentTimelineEvent repository implementation
/// </summary>
public class IncidentTimelineEventRepository(
    ApplicationDbContext context,
    ILogger<IncidentTimelineEventRepository> logger)
    : Repository<IncidentTimelineEvent>(context, logger), IIncidentTimelineEventRepository
{
    public async Task<IEnumerable<IncidentTimelineEvent>> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(e => e.IncidentId == incidentId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IncidentTimelineEvent>> GetByTypeAsync(Guid incidentId, TimelineEventType eventType, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(e => e.IncidentId == incidentId && e.EventType == eventType)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
