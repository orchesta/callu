using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;

namespace Callu.Api.Controllers;

/// <summary>
/// Reporting and analytics endpoints — read-only dashboards and trend data
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/reports")]
[Authorize(Policy = Policies.CanViewReports)]
public class ReportsController(IReportingService reportingService) : ControllerBase
{
    /// <summary>
    /// Incident trends grouped by day/week/month
    /// </summary>
    [HttpGet("incident-trends")]
    public async Task<IActionResult> GetIncidentTrends(
        [FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] string groupBy = "day", CancellationToken ct = default)
    {
        var trends = await reportingService.GetIncidentTrendsAsync(from, to, groupBy, ct);
        return Ok(trends);
    }

    /// <summary>
    /// MTTA and MTTR metrics over time
    /// </summary>
    [HttpGet("mtt-metrics")]
    public async Task<IActionResult> GetMttMetrics(
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct = default)
    {
        var metrics = await reportingService.GetMttMetricsAsync(from, to, ct);
        return Ok(metrics);
    }

    /// <summary>
    /// Service uptime percentages
    /// </summary>
    [HttpGet("service-uptime")]
    public async Task<IActionResult> GetServiceUptime(
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct = default)
    {
        var uptime = await reportingService.GetServiceUptimeAsync(from, to, ct);
        return Ok(uptime);
    }

    /// <summary>
    /// Team response performance
    /// </summary>
    [HttpGet("team-performance")]
    public async Task<IActionResult> GetTeamPerformance(
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct = default)
    {
        var performance = await reportingService.GetTeamPerformanceAsync(from, to, ct);
        return Ok(performance);
    }

    /// <summary>
    /// Incident severity distribution
    /// </summary>
    [HttpGet("severity-distribution")]
    public async Task<IActionResult> GetSeverityDistribution(
        [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct = default)
    {
        var distribution = await reportingService.GetSeverityDistributionAsync(from, to, ct);
        return Ok(distribution);
    }
}
