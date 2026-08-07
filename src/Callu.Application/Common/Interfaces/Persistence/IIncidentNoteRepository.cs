using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// IncidentNote-specific repository interface
/// </summary>
public interface IIncidentNoteRepository : IRepository<IncidentNote>
{
    Task<IEnumerable<IncidentNote>> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<IncidentNote>> GetPinnedByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default);
}
