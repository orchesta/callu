using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// CallLog-specific repository interface
/// </summary>
public interface ICallLogRepository : IRepository<CallLog>
{
    Task<IEnumerable<CallLog>> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default);
}
