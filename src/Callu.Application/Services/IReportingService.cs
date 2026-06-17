using Callu.Shared.Models.Reports;

namespace Callu.Application.Services;

/// <summary>
/// Read-only analytics and reporting service.
/// Queries existing incident data for trends, metrics, and performance reports.
/// </summary>
public interface IReportingService
{
    /// <summary>
    /// Get incident counts grouped by day/week/month within a date range
    /// </summary>
    Task<List<IncidentTrendPointDto>> GetIncidentTrendsAsync(
        DateTime from, DateTime to, string groupBy = "day", CancellationToken ct = default);

    /// <summary>
    /// Get MTTA/MTTR metrics over time
    /// </summary>
    Task<List<MttMetricPointDto>> GetMttMetricsAsync(
        DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Get per-service uptime percentages
    /// </summary>
    Task<List<ServiceUptimeDto>> GetServiceUptimeAsync(
        DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Get per-team acknowledgment/resolution performance
    /// </summary>
    Task<List<TeamPerformanceDto>> GetTeamPerformanceAsync(
        DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Get incident severity distribution
    /// </summary>
    Task<List<SeverityDistributionDto>> GetSeverityDistributionAsync(
        DateTime from, DateTime to, CancellationToken ct = default);
}
