using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Domain.Entities;
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
    AuditCheckpointService auditCheckpoints) : ControllerBase
{
    /// <summary>The public keys an outside verifier needs to check exported signatures.</summary>
    // Readable by anyone who may read the trail: a public key is not a secret, and a verifier that
    // has to be trusted to hand out the key it verifies against has verified nothing.
    [HttpGet("signing-keys")]
    public async Task<IActionResult> GetSigningKeys(CancellationToken ct) =>
        Ok(await auditSigningKeys.GetPublicKeysAsync(ct));

    /// <summary>Issued checkpoints, newest head first.</summary>
    [HttpGet("checkpoints")]
    public async Task<IActionResult> GetCheckpoints(CancellationToken ct) =>
        Ok(await auditCheckpoints.ListIssuedAsync(ct));

    /// <summary>One checkpoint document, exactly as signed.</summary>
    [HttpGet("checkpoints/{id:guid}")]
    [SkipApiResponseWrapper]
    public async Task<IActionResult> GetCheckpoint(Guid id, CancellationToken ct) =>
        await auditCheckpoints.FindIssuedAsync(id, ct) is { } checkpoint
            ? Content(checkpoint.Document, "application/json")
            : NotFound();

    /// <summary>Issues a checkpoint the caller anchors by hand.</summary>
    // The reference is the administrator's statement of where the head is recorded; callu cannot check it,
    // which is why the anchor type says manual.
    [HttpPost("checkpoints")]
    [Authorize(Policy = Policies.CanManageSettings)]
    public async Task<IActionResult> CreateCheckpoint([FromBody] CreateCheckpointRequest request, CancellationToken ct)
    {
        AuditCheckpoint? checkpoint;
        try
        {
            checkpoint = await auditCheckpoints.IssueManualAsync(request.Reference, ct);
        }
        catch (AuditChainIntegrityException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = IntegrityFaultMessage });
        }
        if (checkpoint is null) return Conflict(new { message = "Nothing has been sealed into the audit chain yet." });

        await auditLogService.LogAsync(
            User.FindFirstValue(ClaimTypes.NameIdentifier), AuditAction.Created, "AuditCheckpoint", checkpoint.Id.ToString(),
            newValues: $"headSequence={checkpoint.HeadSequence}; anchor=manual",
            description: "Checkpoint issued", cancellationToken: ct);

        return Ok(new AuditCheckpointSummary(checkpoint.Id, checkpoint.HeadSequence, checkpoint.EventCount,
            checkpoint.AnchorType, checkpoint.AnchorReference, checkpoint.IssuedAt));
    }

    // Not "nothing yet": the stored chain contradicts itself, and the operator has to look before anything is signed over it.
    private const string IntegrityFaultMessage =
        "Audit chain integrity fault: the stored public chain is inconsistent, so nothing was signed or proven. "
        + "Run the audit chain verification and check the server log.";

    /// <summary>An OpenAuditModel inclusion proof for one event.</summary>
    [HttpGet("{id:guid}/proof")]
    [SkipApiResponseWrapper]
    public async Task<IActionResult> GetProof(Guid id, [FromQuery] Guid? checkpointId, CancellationToken ct)
    {
        var result = await auditCheckpoints.ProofAsync(id, checkpointId, ct);
        return result.Status switch
        {
            AuditProofStatus.Found => Content(result.Document!, "application/json"),
            AuditProofStatus.NotSealed => NotFound(new { message = "This entry is not in the public chain yet." }),
            AuditProofStatus.IntegrityFault => StatusCode(StatusCodes.Status500InternalServerError, new { message = IntegrityFaultMessage }),
            _ => NotFound(new { message = "No issued checkpoint covers this entry yet." }),
        };
    }

    /// <summary>How many seal batches an export waits for before it writes what is sealed.</summary>
    // The first export after an upgrade would otherwise run the whole public-chain backfill inside the request.
    public const int ExportSealBatches = 20;

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

        // JSON Lines carries the public chain, so it seals first: a busy installation would otherwise
        // leave up to five minutes of trail out of every file. CSV is the panel's view and does not.
        // Bounded, so a backfill after an upgrade cuts the file short instead of holding the request open.
        long? sealedThrough = null;
        if (jsonLines) sealedThrough = (await auditChain.SealAsync(ct, ExportSealBatches)).PublicHead;

        // Narrowed to one resource, the row says which one — otherwise the trail records that somebody
        // took a file out without saying whose history left with it.
        await auditLogService.LogAsync(
            userId, AuditAction.Exported, "AuditLog", filter.ResourceId?.ToString() ?? string.Empty,
            newValues: $"format={extension}; from={filter.From:O}; to={filter.To:O}; "
                + $"action={filter.Action}; resourceType={filter.ResourceType}; resourceTypes={types}; "
                + $"resourceId={filter.ResourceId}; actorId={filter.ActorId}; requestIpAddress={filter.RequestIpAddress}; "
                + $"query={filter.Query}; sealedThrough={sealedThrough}",
            cancellationToken: ct);

        if (jsonLines)
            await AuditLogExport.WriteJsonLinesAsync(
                Response.Body,
                auditLogService.StreamSealedEventsAsync(filter, sealedThrough!.Value, ct),
                await auditSigningKeys.GetActiveSignerAsync(ct),
                ct);
        else
            await AuditLogExport.WriteCsvAsync(Response.Body, auditLogService.StreamAsync(filter, ct), ct);
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

public record CreateCheckpointRequest(string Reference);
