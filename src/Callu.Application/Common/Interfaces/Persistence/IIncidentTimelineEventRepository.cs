using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// IncidentTimelineEvent-specific repository interface
/// </summary>
public interface IIncidentTimelineEventRepository : IRepository<IncidentTimelineEvent>
{
    Task<IEnumerable<IncidentTimelineEvent>> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<IncidentTimelineEvent>> GetByTypeAsync(Guid incidentId, TimelineEventType eventType, CancellationToken cancellationToken = default);
}
