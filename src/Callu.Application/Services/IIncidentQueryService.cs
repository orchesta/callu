using Callu.Shared.Models.Dashboard;
using Callu.Shared.Models.Incidents;

namespace Callu.Application.Services;

/// <summary>
/// Read-only query service for incident analytics, dashboard summaries,
/// and external alert lookups. Separated from IIncidentService (ISP).
/// </summary>
public interface IIncidentQueryService
{
    /// <summary>
    /// Get incident counts grouped by status
    /// </summary>
    Task<Dictionary<string, int>> GetIncidentCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get aggregated dashboard summary (counts, metrics, recent incidents)
    /// </summary>
    /// <param name="recentCount">How many recent incidents to include</param>
    /// <param name="timeRangeDays">Time range in days (0 = all time)</param>
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(int recentCount = 5, int timeRangeDays = 0, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find an existing unresolved incident by external alert ID (for deduplication)
    /// </summary>
    Task<IncidentDto?> FindByExternalAlertIdAsync(string externalAlertId, CancellationToken cancellationToken = default);

}
