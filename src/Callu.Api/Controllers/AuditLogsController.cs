using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/audit-logs")]
[Authorize(Policy = Policies.CanViewAuditLog)]
public class AuditLogsController(IAuditLogService auditLogService) : ControllerBase
{
    private const int MaxPageSize = 500;
    private const int DefaultPageSize = 100;

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? entityName = null,
        [FromQuery] string? entityId = null,
        [FromQuery] int count = DefaultPageSize,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(count, 1, MaxPageSize);
        var logs = await auditLogService.GetLogsAsync(entityName, entityId, clamped, ct);
        return Ok(logs);
    }
}
