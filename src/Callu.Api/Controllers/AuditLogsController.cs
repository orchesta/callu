using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Api.Filters;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Shared.Models.Audit;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/audit-logs")]
[Authorize(Policy = Policies.CanViewAuditLog)]
public class AuditLogsController(
    IAuditLogService auditLogService,
    AuditChainService auditChain,
    IAuditSigningKeyService auditSigningKeys,
    IWebHostEnvironment hostEnvironment) : ControllerBase
{
    /// <summary>The public keys an outside verifier needs to check exported signatures.</summary>
    // Readable by anyone who may read the trail: a public key is not a secret, and a verifier that
    // has to be trusted to hand out the key it verifies against has verified nothing.
    [HttpGet("signing-keys")]
    public async Task<IActionResult> GetSigningKeys(CancellationToken ct) =>
        Ok(await auditSigningKeys.GetPublicKeysAsync(ct));

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

    /// <summary>The filtered, paged trail. Page and page size are clamped in the service.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] AuditLogFilter filter, CancellationToken ct = default)
    {
        var result = await auditLogService.SearchAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>Streams the filtered trail as CSV or JSON Lines.</summary>
    // The response is written row by row and the export itself is audited: taking the trail out of
    // the product is one of the things a review asks about.
    [HttpGet("export")]
    [SkipApiResponseWrapper]
    public async Task Export(
        [FromQuery] AuditLogFilter filter,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        var jsonLines = string.Equals(format, "jsonl", StringComparison.OrdinalIgnoreCase);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var extension = jsonLines ? "jsonl" : "csv";

        Response.ContentType = jsonLines ? AuditLogExport.JsonLinesContentType : AuditLogExport.CsvContentType;
        Response.Headers.ContentDisposition = $"attachment; filename=\"audit-{stamp}.{extension}\"";

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var types = filter.ResourceTypes is { Count: > 0 } list ? string.Join('|', list) : null;

        // Narrowed to one resource, the row says which one — otherwise the trail records that somebody
        // took a file out without saying whose history left with it.
        await auditLogService.LogAsync(
            userId, AuditAction.Exported, "AuditLog", filter.ResourceId?.ToString() ?? string.Empty,
            newValues: $"format={extension}; from={filter.From:O}; to={filter.To:O}; "
                + $"action={filter.Action}; resourceType={filter.ResourceType}; resourceTypes={types}; "
                + $"resourceId={filter.ResourceId}; actorId={filter.ActorId}; requestIpAddress={filter.RequestIpAddress}; "
                + $"query={filter.Query}",
            cancellationToken: ct);

        var rows = auditLogService.StreamAsync(filter, ct);

        if (jsonLines)
            await AuditLogExport.WriteJsonLinesAsync(
                Response.Body,
                rows,
                hostEnvironment.EnvironmentName,
                await auditSigningKeys.GetActiveSignerAsync(ct),
                ct);
        else
            await AuditLogExport.WriteCsvAsync(Response.Body, rows, ct);
    }

    /// <summary>Replays the hash chain and reports the first entry that does not fit.</summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(CancellationToken ct = default)
    {
        var verdict = await auditChain.VerifyAsync(ct);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await auditLogService.LogAsync(
            userId,
            verdict.Intact ? AuditAction.IntegrityVerified : AuditAction.IntegrityBroken,
            "AuditLog",
            (verdict.FirstBrokenSequence ?? 0).ToString(),
            description: verdict.Reason,
            newValues: $"checked={verdict.CheckedCount}",
            cancellationToken: ct);

        return Ok(new
        {
            intact = verdict.Intact,
            checkedCount = verdict.CheckedCount,
            firstBrokenSequence = verdict.FirstBrokenSequence,
            reason = verdict.Reason,
        });
    }
}
