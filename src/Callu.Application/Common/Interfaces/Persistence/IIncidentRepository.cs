using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Incident-specific repository interface
/// </summary>
public interface IIncidentRepository : IRepository<Incident>
{
    Task<IEnumerable<Incident>> GetActiveIncidentsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Incident>> GetByStatusAsync(IncidentStatus status, CancellationToken cancellationToken = default);
    Task<IEnumerable<Incident>> GetBySeverityAsync(IncidentSeverity severity, CancellationToken cancellationToken = default);
    Task<IEnumerable<Incident>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Incident>> GetByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<Incident?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Incident?> GetWithServiceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default);
}
