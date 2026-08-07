using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// CallLog-specific repository interface
/// </summary>
public interface ICallLogRepository : IRepository<CallLog>
{
    Task<IEnumerable<CallLog>> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>Whether any call has been recorded under this provider call id.</summary>
    Task<bool> AnyWithCallTokenAsync(string callToken, CancellationToken cancellationToken = default);

    /// <summary>Whether a call is already on record for this page attempt, whichever provider placed it.</summary>
    Task<bool> AnyForAttemptAsync(Guid attemptId, CancellationToken cancellationToken = default);
}
