using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Callu.Application.Services;
using Callu.Shared.Exceptions;
using Callu.Shared.Localization;
using Callu.Shared.Models.Postmortems;

namespace Callu.Api.Controllers;

/// <summary>
/// Postmortem documents — root cause analysis linked to incidents.
/// Read endpoints require CanViewPostmortems; mutations require CanManagePostmortems.
/// Bare [Authorize] would let Viewers publish/delete (fix 09.P0-1).
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/postmortems")]
public class PostmortemsController(IPostmortemService postmortemService) : ControllerBase
{
    [HttpGet, Authorize(Policy = Policies.CanViewPostmortems)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await postmortemService.GetAllAsync(ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}"), Authorize(Policy = Policies.CanViewPostmortems)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var item = await postmortemService.GetByIdAsync(id, ct);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpGet("by-incident/{incidentId:guid}"), Authorize(Policy = Policies.CanViewPostmortems)]
    public async Task<IActionResult> GetByIncident(Guid incidentId, CancellationToken ct)
    {
        var items = await postmortemService.GetByIncidentAsync(incidentId, ct);
        return Ok(items);
    }

    [HttpPost, Authorize(Policy = Policies.CanManagePostmortems)]
    public async Task<IActionResult> Create([FromBody] CreatePostmortemRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var item = await postmortemService.CreateAsync(request, userId, ct);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.CanManagePostmortems)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePostmortemRequest request, CancellationToken ct)
    {
        try
        {
            var success = await postmortemService.UpdateAsync(id, request, ct);
            if (!success) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }
    }

    [HttpPost("{id:guid}/submit"), Authorize(Policy = Policies.CanManagePostmortems)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        try
        {
            var success = await postmortemService.SubmitForReviewAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex) { throw new ConflictException(ex.Message); }
    }

    [HttpPost("{id:guid}/reject"), Authorize(Policy = Policies.CanManagePostmortems)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        try
        {
            var success = await postmortemService.RejectReviewAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex) { throw new ConflictException(ex.Message); }
    }

    [HttpPost("{id:guid}/publish"), Authorize(Policy = Policies.CanManagePostmortems)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        try
        {
            var success = await postmortemService.PublishAsync(id, ct);
            if (!success) return NotFound();
            return Ok(new { message = Messages.Get("postmortems.published") });
        }
        catch (InvalidOperationException ex) { throw new ConflictException(ex.Message); }
    }

    [HttpPost("{id:guid}/lock"), Authorize(Policy = Policies.CanManagePostmortems)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken ct)
    {
        try
        {
            var success = await postmortemService.LockAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex) { throw new ConflictException(ex.Message); }
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = Policies.CanManagePostmortems)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            var success = await postmortemService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex) { throw new ConflictException(ex.Message); }
    }
}
