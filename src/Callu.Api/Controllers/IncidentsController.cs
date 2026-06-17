using Asp.Versioning;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Exceptions;
using Callu.Shared.Localization;
using Callu.Shared.Models.Incidents;
using Microsoft.Extensions.Logging;

namespace Callu.Api.Controllers;

/// <summary>
/// Incident management — CRUD, workflow actions, timeline
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class IncidentsController(
    IIncidentService incidentService,
    IIncidentNoteService noteService,
    ICallLogService callLogService,
    ILogger<IncidentsController> logger) : ControllerBase
{
    /// <summary>
    /// Get incidents with filtering and pagination
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Policies.CanViewIncidents)]
    public async Task<IActionResult> GetIncidents([FromQuery] IncidentFilter filter, CancellationToken cancellationToken)
    {
        var result = await incidentService.GetIncidentsPagedAsync(filter, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get incident by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewIncidents)]
    public async Task<IActionResult> GetIncident(Guid id, CancellationToken cancellationToken)
    {
        var incident = await incidentService.GetIncidentByIdAsync(id, cancellationToken);
        if (incident == null) return NotFound();
        return Ok(incident);
    }

    /// <summary>
    /// List outbound webhook delivery attempts (ACK callbacks) for this incident.
    /// Newest-first; default limit 20, max 100. Surfaces retry status and failure
    /// reasons so operators can see what happened without trawling logs. Fix 10.P1-7.
    /// </summary>
    [HttpGet("{id:guid}/webhook-deliveries")]
    [Authorize(Policy = Policies.CanViewIncidents)]
    public async Task<IActionResult> GetWebhookDeliveries(Guid id, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        var rows = await incidentService.GetWebhookDeliveriesAsync(id, limit, cancellationToken);
        return Ok(rows);
    }

    /// <summary>
    /// Create a new incident
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> CreateIncident([FromBody] CreateIncidentRequest request, CancellationToken cancellationToken)
    {
        var result = await incidentService.CreateIncidentAsync(request, cancellationToken);

        if (result.Outcome == IncidentCreateOutcome.Suppressed)
            return Accepted(result);

        return CreatedAtAction(nameof(GetIncident), new { id = result.Incident!.Id }, result.Incident);
    }

    /// <summary>
    /// Update an existing incident
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> UpdateIncident(Guid id, [FromBody] UpdateIncidentRequest request, CancellationToken cancellationToken)
    {
        await incidentService.UpdateIncidentAsync(id, request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Acknowledge an incident
    /// </summary>
    [HttpPost("{id:guid}/acknowledge")]
    [Authorize(Policy = Policies.CanAcknowledgeIncidents)]
    public async Task<IActionResult> AcknowledgeIncident(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await incidentService.AcknowledgeIncidentAsync(id, userId, cancellationToken);
        return Ok(new { message = Messages.Get("incidents.acknowledged") });
    }

    /// <summary>
    /// Resolve an incident
    /// </summary>
    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = Policies.CanResolveIncidents)]
    public async Task<IActionResult> ResolveIncident(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await incidentService.ResolveIncidentAsync(id, userId, cancellationToken);
        return Ok(new { message = Messages.Get("incidents.resolved") });
    }

    /// <summary>
    /// Close a resolved incident (terminal — no more transitions allowed).
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = Policies.CanResolveIncidents)]
    public async Task<IActionResult> CloseIncident(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await incidentService.CloseIncidentAsync(id, userId, cancellationToken);
        return Ok(new { message = Messages.Get("incidents.closed") });
    }

    /// <summary>
    /// Reopen a resolved or closed incident back to Open.
    /// </summary>
    [HttpPost("{id:guid}/reopen")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> ReopenIncident(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await incidentService.ReopenIncidentAsync(id, userId, cancellationToken);
        return Ok(new { message = Messages.Get("incidents.reopened") });
    }

    /// <summary>
    /// Delete an incident (soft delete)
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> DeleteIncident(Guid id, CancellationToken cancellationToken)
    {
        await incidentService.DeleteIncidentAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Get all notes for an incident
    /// </summary>
    [HttpGet("{id:guid}/notes")]
    [Authorize(Policy = Policies.CanViewIncidents)]
    public async Task<IActionResult> GetNotes(Guid id, CancellationToken cancellationToken)
    {
        var notes = await noteService.GetNotesAsync(id, cancellationToken);
        return Ok(notes);
    }

    /// <summary>
    /// Add a note to an incident
    /// </summary>
    [HttpPost("{id:guid}/notes")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> AddNote(Guid id, [FromBody] CreateIncidentNoteRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var note = await noteService.AddNoteAsync(id, request, userId, cancellationToken);
        return Ok(note);
    }

    /// <summary>
    /// Update a note
    /// </summary>
    [HttpPut("notes/{noteId:guid}")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> UpdateNote(Guid noteId, [FromBody] UpdateIncidentNoteRequest request, CancellationToken cancellationToken)
    {
        var success = await noteService.UpdateNoteAsync(noteId, request, cancellationToken);
        if (!success) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Delete a note
    /// </summary>
    [HttpDelete("notes/{noteId:guid}")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> DeleteNote(Guid noteId, CancellationToken cancellationToken)
    {
        var success = await noteService.DeleteNoteAsync(noteId, cancellationToken);
        if (!success) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Manually escalate an incident
    /// </summary>
    [HttpPost("{id:guid}/escalate")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> EscalateIncident(Guid id, [FromBody] EscalateRequest? request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await incidentService.EscalateIncidentAsync(id, userId, request?.Reason, cancellationToken);
        return Ok(new { message = Messages.Get("incidents.escalated") });
    }

    /// <summary>
    /// Reassign an incident to a different user
    /// </summary>
    [HttpPut("{id:guid}/assign")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> ReassignIncident(Guid id, [FromBody] ReassignRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await incidentService.ReassignIncidentAsync(id, request.TargetUserId, userId, cancellationToken);
        return Ok(new { message = Messages.Get("incidents.reassigned") });
    }

    /// <summary>
    /// Get call logs for an incident (convenience endpoint)
    /// </summary>
    [HttpGet("{id:guid}/timeline")]
    public async Task<IActionResult> GetTimeline(Guid id, CancellationToken cancellationToken)
    {
        var incident = await incidentService.GetIncidentByIdAsync(id, cancellationToken);
        if (incident == null) return NotFound();
        
        var notes = await noteService.GetNotesAsync(id, cancellationToken);
        var events = await callLogService.GetTimelineEventsAsync(id, cancellationToken);
        return Ok(new { incident, notes, events });
    }

    /// <summary>
    /// Gets the active video conference for the incident
    /// </summary>
    [HttpGet("{id:guid}/conference")]
    [Authorize(Policy = Policies.CanViewIncidents)]
    public async Task<IActionResult> GetActiveConference(
        Guid id, 
        [FromServices] IVideoConferenceService videoConferenceService, 
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var conference = await videoConferenceService.GetActiveConferenceForUserAsync(id, userId, cancellationToken);
        
        if (conference == null)
            return NoContent(); 
            
        return Ok(conference);
    }

    /// <summary>
    /// Bulk acknowledge multiple incidents
    /// </summary>
    [HttpPost("bulk/acknowledge")]
    [Authorize(Policy = Policies.CanAcknowledgeIncidents)]
    public async Task<IActionResult> BulkAcknowledge([FromBody] BulkActionRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return await BulkApplyAsync(request.IncidentIds, "acknowledge",
            id => incidentService.AcknowledgeIncidentAsync(id, userId, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Bulk resolve multiple incidents
    /// </summary>
    [HttpPost("bulk/resolve")]
    [Authorize(Policy = Policies.CanResolveIncidents)]
    public async Task<IActionResult> BulkResolve([FromBody] BulkActionRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return await BulkApplyAsync(request.IncidentIds, "resolve",
            id => incidentService.ResolveIncidentAsync(id, userId, cancellationToken), cancellationToken);
    }

    private async Task<IActionResult> BulkApplyAsync(
        IReadOnlyList<Guid> ids, string verb, Func<Guid, Task> action, CancellationToken cancellationToken)
    {
        var results = new List<object>(ids.Count);
        var succeeded = 0;
        var failed = 0;
        foreach (var id in ids)
        {
            try
            {
                await action(id);
                succeeded++;
                results.Add(new { id, ok = true });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                var code = ex switch
                {
                    NotFoundException => "not_found",
                    ConflictException => "conflict",
                    ForbiddenException => "forbidden",
                    _ => "error",
                };
                logger.LogWarning(ex, "Bulk {Verb} failed for incident {IncidentId}", verb, id);
                results.Add(new { id, ok = false, code, error = ex.Message });
            }
        }
        return Ok(new { succeeded, failed, total = ids.Count, results });
    }
}

/// <summary>
/// Request body for bulk incident actions
/// </summary>
public record BulkActionRequest(List<Guid> IncidentIds);
