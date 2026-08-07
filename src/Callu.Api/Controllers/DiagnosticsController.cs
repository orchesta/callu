using Asp.Versioning;
using Callu.Application.Services;
using Callu.Shared;
using Callu.Shared.Models.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/diagnostics")]
[Authorize(Policy = Policies.CanManageSettings)]
public class DiagnosticsController(ITracingQueryService tracingQueryService) : ControllerBase
{
    /// <summary>Build identity for this API process — SemVer plus InformationalVersion (often +git SHA).</summary>
    [HttpGet("build")]
    public IActionResult GetBuild()
    {
        var build = BuildIdentity.Current;
        return Ok(new
        {
            version = build.Version,
            informationalVersion = build.InformationalVersion,
        });
    }

    [HttpGet("tracing/status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
        => Ok(await tracingQueryService.GetStatusAsync(ct));

    [HttpGet("tracing/traces")]
    public async Task<IActionResult> Search(
        [FromQuery] string? service = null,
        [FromQuery] string? operation = null,
        [FromQuery] int lookbackMinutes = 60,
        [FromQuery] int limit = 50,
        [FromQuery] bool onlyErrors = false,
        [FromQuery] int? minDurationMs = null,
        CancellationToken ct = default)
    {
        var request = new TraceSearchRequest
        {
            Service = service,
            Operation = operation,
            LookbackMinutes = lookbackMinutes,
            Limit = limit,
            OnlyErrors = onlyErrors,
            MinDurationMs = minDurationMs,
        };

        return Ok(await tracingQueryService.SearchAsync(request, ct));
    }

    [HttpGet("tracing/overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? service = null,
        [FromQuery] int lookbackMinutes = 60,
        CancellationToken ct = default)
    {
        var request = new TraceSearchRequest { Service = service, LookbackMinutes = lookbackMinutes };
        return Ok(await tracingQueryService.GetOverviewAsync(request, ct));
    }

    [HttpGet("tracing/traces/{traceId}")]
    public async Task<IActionResult> GetTrace(string traceId, CancellationToken ct)
    {
        var trace = await tracingQueryService.GetTraceAsync(traceId, ct);
        return trace is null ? NotFound() : Ok(trace);
    }
}
