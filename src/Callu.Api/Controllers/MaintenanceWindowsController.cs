using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Models.Maintenance;

namespace Callu.Api.Controllers;

/// <summary>
/// Maintenance windows — scheduled suppression of alerts
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/maintenance-windows")]
[Authorize(Policy = Policies.CanManageSettings)]
public class MaintenanceWindowsController(IMaintenanceWindowService maintenanceService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await maintenanceService.GetAllAsync(ct);
        return Ok(items);
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var items = await maintenanceService.GetActiveAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMaintenanceWindowRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var item = await maintenanceService.CreateAsync(request, userId, ct);
        return Created($"/api/v1/maintenance-windows/{item.Id}", item);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var success = await maintenanceService.CancelAsync(id, ct);
        if (!success) return NotFound();
        return Ok(new { message = Messages.Get("maintenance.cancelled") });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await maintenanceService.DeleteAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }
}
