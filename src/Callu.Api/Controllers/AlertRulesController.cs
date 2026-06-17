using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Models.AlertRules;

namespace Callu.Api.Controllers;

/// <summary>
/// Alert automation rules — define conditions that trigger actions on incidents
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/alert-rules")]
[Authorize(Policy = Policies.CanManageSettings)]
public class AlertRulesController(
    IAlertRuleService alertRuleService,
    INotificationPushService pushService) : ControllerBase
{
    /// <summary>
    /// Get all alert rules ordered by priority
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var rules = await alertRuleService.GetRulesAsync(ct);
        return Ok(rules);
    }

    /// <summary>
    /// Get a specific alert rule by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var rule = await alertRuleService.GetRuleAsync(id, ct);
        if (rule == null) return NotFound();
        return Ok(rule);
    }

    /// <summary>
    /// Create a new alert rule
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAlertRuleRequest request, CancellationToken ct)
    {
        var rule = await alertRuleService.CreateRuleAsync(request, ct);
        await pushService.BroadcastSettingsUpdatedAsync("alert-rules", ct);
        return CreatedAtAction(nameof(GetById), new { id = rule.Id }, rule);
    }

    /// <summary>
    /// Update an existing alert rule
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAlertRuleRequest request, CancellationToken ct)
    {
        var success = await alertRuleService.UpdateRuleAsync(id, request, ct);
        if (!success) return NotFound();
        await pushService.BroadcastSettingsUpdatedAsync("alert-rules", ct);
        return NoContent();
    }

    /// <summary>
    /// Delete an alert rule
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await alertRuleService.DeleteRuleAsync(id, ct);
        if (!success) return NotFound();
        await pushService.BroadcastSettingsUpdatedAsync("alert-rules", ct);
        return NoContent();
    }

    /// <summary>
    /// Toggle an alert rule's enabled state
    /// </summary>
    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id, CancellationToken ct)
    {
        var success = await alertRuleService.ToggleRuleAsync(id, ct);
        if (!success) return NotFound();
        await pushService.BroadcastSettingsUpdatedAsync("alert-rules", ct);
        return Ok(new { message = Messages.Get("alertRules.toggled") });
    }

    /// <summary>
    /// Get static metadata: condition fields, operators, action types, severity values
    /// </summary>
    [HttpGet("metadata")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult GetMetadata()
    {
        return Ok(new
        {
            conditionFields = new[]
            {
                new { value = "Severity", label = "Severity" },
                new { value = "Status", label = "Status" },
                new { value = "Title", label = "Title" },
                new { value = "Description", label = "Description" },
                new { value = "Service", label = "Service ID" },
                new { value = "Team", label = "Team ID" },
                new { value = "Source", label = "Source Integration" },
            },
            conditionOperators = new[]
            {
                new { value = "Equals", label = "Equals" },
                new { value = "NotEquals", label = "Not Equals" },
                new { value = "Contains", label = "Contains" },
                new { value = "NotContains", label = "Not Contains" },
                new { value = "GreaterThan", label = "Greater Than (Severity)" },
                new { value = "LessThan", label = "Less Than (Severity)" },
            },
            actionTypes = new[]
            {
                new { value = "AutoEscalate", label = "Auto-Escalate" },
                new { value = "AssignTeam", label = "Assign to Team" },
                new { value = "AssignUser", label = "Assign to User" },
                new { value = "SetSeverity", label = "Set Severity" },
                new { value = "AddNote", label = "Add Note" },
                new { value = "SuppressNotification", label = "Suppress Notifications" },
            },
            severityValues = new[] { "Critical", "High", "Medium", "Low" },
        });
    }
}
