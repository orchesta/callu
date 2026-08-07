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

    /// <summary>Aggregated dashboard summary (counts, metrics, recent incidents); timeRangeDays 0 means all time.</summary>
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(int recentCount = 5, int timeRangeDays = 0, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find an existing unresolved incident by external alert ID (for deduplication)
    /// </summary>
    Task<IncidentDto?> FindByExternalAlertIdAsync(string externalAlertId, CancellationToken cancellationToken = default);

}
