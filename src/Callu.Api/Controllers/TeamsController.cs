using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Models.Teams;
using Callu.Shared.Results;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/teams")]
[Authorize]
public class TeamsController(
    ITeamService teamService,
    INotificationPushService pushService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.CanViewTeams)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var teams = await teamService.GetTeamsAsync(ct);
        return Ok(teams);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewTeams)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var team = await teamService.GetTeamByIdAsync(id, ct);
        if (team == null) return NotFound();
        return Ok(team);
    }

    [HttpPost]
    [Authorize(Policy = Policies.CanManageTeams)]
    public async Task<IActionResult> Create([FromBody] CreateTeamRequest request, CancellationToken ct)
    {
        var team = await teamService.CreateTeamAsync(request, ct);
        await pushService.BroadcastTeamUpdatedAsync(team.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = team.Id }, team);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageTeams)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeamRequest request, CancellationToken ct)
    {
        var success = await teamService.UpdateTeamAsync(id, request, ct);
        if (!success) return NotFound();
        await pushService.BroadcastTeamUpdatedAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.CanManageTeams)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await teamService.DeleteTeamAsync(id, ct);
        if (!success) return NotFound();
        await pushService.BroadcastTeamUpdatedAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/members")]
    [Authorize(Policy = Policies.CanManageTeams)]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest request, CancellationToken ct)
    {
        var success = await teamService.AddMemberAsync(id, request.UserId, request.Role, ct);
        if (!success) return BadRequest(ApiResponse.Fail(Messages.Get("teams.memberAddFailed")));
        await pushService.BroadcastTeamUpdatedAsync(id, ct);
        return Ok(new { message = Messages.Get("teams.memberAdded") });
    }

    [HttpDelete("{teamId:guid}/members/{memberId:guid}")]
    [Authorize(Policy = Policies.CanManageTeams)]
    public async Task<IActionResult> RemoveMember(Guid teamId, Guid memberId, CancellationToken ct)
    {
        var success = await teamService.RemoveMemberAsync(teamId, memberId, ct);
        if (!success) return NotFound();
        await pushService.BroadcastTeamUpdatedAsync(teamId, ct);
        return NoContent();
    }

    [HttpPut("{teamId:guid}/members/{memberId:guid}/role")]
    [Authorize(Policy = Policies.CanManageTeams)]
    public async Task<IActionResult> UpdateMemberRole(Guid teamId, Guid memberId, [FromBody] UpdateMemberRoleRequest request, CancellationToken ct)
    {
        var success = await teamService.UpdateMemberRoleAsync(teamId, memberId, request.Role, ct);
        if (!success) return NotFound();
        await pushService.BroadcastTeamUpdatedAsync(teamId, ct);
        return NoContent();
    }
}

