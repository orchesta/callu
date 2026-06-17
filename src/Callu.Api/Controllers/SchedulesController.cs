using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Models.Schedules;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/schedules")]
[Authorize]
public class SchedulesController(
    IScheduleService scheduleService,
    IRotationService rotationService,
    IOnCallOverrideService overrideService,
    IOnCallService onCallService,
    INotificationPushService pushService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.CanViewSchedules)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var schedules = await scheduleService.GetSchedulesAsync(ct);
        return Ok(schedules);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewSchedules)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var schedule = await scheduleService.GetScheduleByIdAsync(id, ct);
        if (schedule == null) return NotFound();
        return Ok(schedule);
    }

    [HttpPost]
    [Authorize(Policy = Policies.CanManageSchedules)]
    public async Task<IActionResult> Create([FromBody] CreateScheduleRequest request, CancellationToken ct)
    {
        var schedule = await scheduleService.CreateScheduleAsync(request, ct);
        await pushService.BroadcastScheduleUpdatedAsync(schedule.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = schedule.Id }, schedule);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageSchedules)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateScheduleRequest request, CancellationToken ct)
    {
        var success = await scheduleService.UpdateScheduleAsync(id, request, ct);
        if (!success) return NotFound();
        await pushService.BroadcastScheduleUpdatedAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.CanManageSchedules)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await scheduleService.DeleteScheduleAsync(id, ct);
        if (!success) return NotFound();
        await pushService.BroadcastScheduleUpdatedAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/rotations")]
    [Authorize(Policy = Policies.CanViewSchedules)]
    public async Task<IActionResult> GetRotations(Guid id, CancellationToken ct)
    {
        var rotations = await rotationService.GetRotationsAsync(id, ct);
        return Ok(rotations);
    }

    [HttpPost("{id:guid}/rotations")]
    [Authorize(Policy = Policies.CanManageSchedules)]
    public async Task<IActionResult> AddRotation(Guid id, [FromBody] CreateRotationRequest request, CancellationToken ct)
    {
        var rotation = await rotationService.AddRotationAsync(id, request, ct);
        await pushService.BroadcastScheduleUpdatedAsync(id, ct);
        return Ok(rotation);
    }

    [HttpPut("rotations/{rotationId:guid}")]
    [Authorize(Policy = Policies.CanManageSchedules)]
    public async Task<IActionResult> UpdateRotation(Guid rotationId, [FromBody] UpdateRotationRequest request, CancellationToken ct)
    {
        var scheduleId = await rotationService.UpdateRotationAsync(rotationId, request, ct);
        if (scheduleId is null) return NotFound();
        await pushService.BroadcastScheduleUpdatedAsync(scheduleId.Value, ct);
        return NoContent();
    }

    [HttpDelete("rotations/{rotationId:guid}")]
    [Authorize(Policy = Policies.CanManageSchedules)]
    public async Task<IActionResult> RemoveRotation(Guid rotationId, CancellationToken ct)
    {
        var scheduleId = await rotationService.RemoveRotationAsync(rotationId, ct);
        if (scheduleId is null) return NotFound();
        await pushService.BroadcastScheduleUpdatedAsync(scheduleId.Value, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/overrides")]
    [Authorize(Policy = Policies.CanViewSchedules)]
    public async Task<IActionResult> GetOverrides(Guid id, CancellationToken ct)
    {
        var overrides = await overrideService.GetOverridesAsync(id, ct);
        return Ok(overrides);
    }

    [HttpPost("overrides")]
    [Authorize(Policy = Policies.CanManageSchedules)]
    public async Task<IActionResult> CreateOverride([FromBody] CreateOverrideRequest request, CancellationToken ct)
    {
        var result = await overrideService.CreateOverrideAsync(request, ct);
        await pushService.BroadcastScheduleUpdatedAsync(result.ScheduleId, ct);
        return Ok(result);
    }

    [HttpPut("overrides/{overrideId:guid}")]
    [Authorize(Policy = Policies.CanManageSchedules)]
    public async Task<IActionResult> UpdateOverride(Guid overrideId, [FromBody] UpdateOverrideRequest request, CancellationToken ct)
    {
        var scheduleId = await overrideService.UpdateOverrideAsync(overrideId, request, ct);
        if (scheduleId is null) return NotFound();
        await pushService.BroadcastScheduleUpdatedAsync(scheduleId.Value, ct);
        return NoContent();
    }

    [HttpDelete("overrides/{overrideId:guid}")]
    [Authorize(Policy = Policies.CanManageSchedules)]
    public async Task<IActionResult> DeleteOverride(Guid overrideId, CancellationToken ct)
    {
        var scheduleId = await overrideService.DeleteOverrideAsync(overrideId, ct);
        if (scheduleId is null) return NotFound();
        await pushService.BroadcastScheduleUpdatedAsync(scheduleId.Value, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/on-call")]
    public async Task<IActionResult> GetCurrentOnCall(Guid id, CancellationToken ct)
    {
        var status = await onCallService.GetCurrentOnCallAsync(id, ct);
        return Ok(status);
    }

    [HttpGet("{id:guid}/occurrences")]
    [Authorize(Policy = Policies.CanViewSchedules)]
    public async Task<IActionResult> GetUpcomingOccurrences(Guid id, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        var occurrences = await rotationService.GetUpcomingRotationsAsync(id, days, ct);
        return Ok(occurrences);
    }
}
