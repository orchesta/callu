using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/call-logs")]
[Authorize(Policy = Policies.CanViewCallLogs)]
public class CallLogsController(ICallLogService callLogService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var (items, total) = await callLogService.GetCallLogsPagedAsync(page, pageSize, ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("incident/{incidentId:guid}")]
    public async Task<IActionResult> GetByIncident(Guid incidentId, CancellationToken ct)
    {
        var logs = await callLogService.GetCallLogsByIncidentIdAsync(incidentId, ct);
        return Ok(logs);
    }

    [HttpGet("incident/{incidentId:guid}/timeline")]
    public async Task<IActionResult> GetTimeline(Guid incidentId, CancellationToken ct)
    {
        var events = await callLogService.GetTimelineEventsAsync(incidentId, ct);
        return Ok(events);
    }
}
