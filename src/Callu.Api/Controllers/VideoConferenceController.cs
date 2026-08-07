using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Results;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/conferences")]
[Authorize]
public class VideoConferenceController(IVideoConferenceService conferenceService) : ControllerBase
{
    [HttpPost("rooms/{incidentId:guid}")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> CreateRoom(Guid incidentId, CancellationToken ct)
    {
        var result = await conferenceService.CreateRoomAsync(incidentId, ct);
        if (!result.Success) return BadRequest(ApiResponse.Fail(result.Error ?? "Operation failed"));
        return Ok(result);
    }

    [HttpGet("validate/{participantToken}")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateParticipant(string participantToken, CancellationToken ct)
    {
        var info = await conferenceService.ValidateParticipantAsync(participantToken, ct);
        if (info == null) return NotFound(ApiResponse.Fail(Messages.Get("conference.invalidToken")));
        return Ok(info);
    }

    [HttpPost("join/{participantToken}")]
    [AllowAnonymous]
    public async Task<IActionResult> JoinConference(string participantToken, CancellationToken ct)
    {
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var result = await conferenceService.JoinConferenceAsync(participantToken, sourceIp, userAgent, ct);
        return Ok(result);
    }

    [HttpPost("leave/{participantToken}")]
    [AllowAnonymous]
    public async Task<IActionResult> LeaveConference(string participantToken, CancellationToken ct)
    {
        await conferenceService.LeaveConferenceAsync(participantToken, ct);
        return Ok(new { message = Messages.Get("conference.left") });
    }

    [HttpPost("rooms/{roomId:guid}/end")]
    [Authorize(Policy = Policies.CanManageIncidents)]
    public async Task<IActionResult> EndConference(Guid roomId, CancellationToken ct)
    {
        await conferenceService.EndConferenceAsync(roomId, ct);
        return Ok(new { message = Messages.Get("conference.ended") });
    }
}
