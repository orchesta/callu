using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;

namespace Callu.Api.Controllers;

/// <summary>
/// Dashboard analytics and summary endpoints
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Policy = Policies.CanViewReports)]
public class DashboardController(
    IIncidentQueryService incidentQueryService,
    IServiceCatalogService serviceCatalogService) : ControllerBase
{
    /// <summary>
    /// Get full dashboard summary (counts, MTTA/MTTR, recent incidents)
    /// </summary>
    /// <param name="recentCount">How many recent incidents to return (default: 5)</param>
    /// <param name="timeRangeDays">Time range in days (default: 0 = all time). Common values: 1, 7, 30</param>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int recentCount = 5, [FromQuery] int timeRangeDays = 0, CancellationToken cancellationToken = default)
    {
        var summary = await incidentQueryService.GetDashboardSummaryAsync(recentCount, timeRangeDays, cancellationToken);
        return Ok(summary);
    }

    /// <summary>
    /// Get incident counts grouped by status
    /// </summary>
    [HttpGet("incident-counts")]
    public async Task<IActionResult> GetIncidentCounts(CancellationToken cancellationToken)
    {
        var counts = await incidentQueryService.GetIncidentCountsAsync(cancellationToken);
        return Ok(counts);
    }

    /// <summary>
    /// Get service catalog for system health overview
    /// </summary>
    [HttpGet("system-health")]
    public async Task<IActionResult> GetSystemHealth(CancellationToken cancellationToken)
    {
        var services = await serviceCatalogService.GetServicesAsync(cancellationToken);
        return Ok(services);
    }
}
