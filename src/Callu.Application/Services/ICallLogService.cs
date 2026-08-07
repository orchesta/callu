using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Incidents;

namespace Callu.Application.Services;

/// <summary>
/// Service for querying call logs and incident timeline events.
/// Replaces direct IDbContextFactory usage in pages.
/// </summary>
public interface ICallLogService
{
    /// <summary>
    /// Get call logs for a specific incident, ordered by most recent first.
    /// </summary>
    Task<IEnumerable<CallLogDto>> GetCallLogsByIncidentIdAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get paged call logs across all incidents.
    /// </summary>
    Task<(IEnumerable<CallLogDto> Items, int TotalCount)> GetCallLogsPagedAsync(
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get timeline events for a specific incident (call-related events).
    /// </summary>
    Task<IEnumerable<IncidentTimelineEventDto>> GetTimelineEventsAsync(Guid incidentId, CancellationToken cancellationToken = default);
}
