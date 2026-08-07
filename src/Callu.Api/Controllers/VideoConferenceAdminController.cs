using Asp.Versioning;
using Callu.Application.Services;
using Callu.Shared.Models.Conference;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Callu.Api.Controllers;

/// <summary>Admin paginated list endpoint for video conferences.</summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/conferences")]
[Authorize(Policy = Policies.CanViewIncidents)]
public class VideoConferenceAdminController(IVideoConferenceService videoConferenceService) : ControllerBase
{
    /// <summary>
    /// Gets a paginated list of video conferences
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetConferenceRooms(
        [FromQuery] ConferenceRoomFilter filter, 
        CancellationToken ct)
    {
        var (items, total) = await videoConferenceService.GetConferenceRoomsPagedAsync(filter, ct);
        var pagedResult = Callu.Shared.Results.PagedResult<ConferenceRoomDto>.Create(items, total, filter.Page, filter.PageSize);
        return Ok(pagedResult);
    }
}
