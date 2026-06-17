using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Models.Runbooks;

namespace Callu.Api.Controllers;

/// <summary>
/// Operational runbooks — step-by-step procedures for incident response.
/// Read endpoints require CanViewRunbooks; mutations require CanManageRunbooks.
/// Bare [Authorize] would let Viewers create/edit/delete (fix 09.P0-1).
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/runbooks")]
public class RunbooksController(IRunbookService runbookService) : ControllerBase
{
    [HttpGet, Authorize(Policy = Policies.CanViewRunbooks)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await runbookService.GetAllAsync(ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}"), Authorize(Policy = Policies.CanViewRunbooks)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var item = await runbookService.GetByIdAsync(id, ct);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpGet("by-service/{serviceId:guid}"), Authorize(Policy = Policies.CanViewRunbooks)]
    public async Task<IActionResult> GetByService(Guid serviceId, CancellationToken ct)
    {
        var items = await runbookService.GetByServiceAsync(serviceId, ct);
        return Ok(items);
    }

    [HttpPost, Authorize(Policy = Policies.CanManageRunbooks)]
    public async Task<IActionResult> Create([FromBody] CreateRunbookRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var item = await runbookService.CreateAsync(request, userId, ct);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.CanManageRunbooks)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRunbookRequest request, CancellationToken ct)
    {
        var success = await runbookService.UpdateAsync(id, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/mark-used"), Authorize(Policy = Policies.CanViewRunbooks)]
    public async Task<IActionResult> MarkUsed(Guid id, CancellationToken ct)
    {
        var success = await runbookService.MarkUsedAsync(id, ct);
        if (!success) return NotFound();
        return Ok(new { message = Messages.Get("runbooks.usageRecorded") });
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = Policies.CanManageRunbooks)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await runbookService.DeleteAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }
}
