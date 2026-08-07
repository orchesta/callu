using System.Security.Claims;
using Asp.Versioning;
using Callu.Application.Services;
using Callu.Shared.Models.Devices;
using Callu.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/devices")]
[Authorize]
public class DevicesController(IPushDeviceService pushDevices) : ControllerBase
{
    [HttpPost("push")]
    public async Task<IActionResult> RegisterPush(
        [FromBody] RegisterPushDeviceRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var device = await pushDevices.RegisterAsync(userId, request, ct);
        return Ok(device);
    }

    /// <summary>Optional body <c>{ "pushToken" }</c>; omit to clear every device for this user.</summary>
    [HttpDelete("push")]
    public Task<IActionResult> UnregisterPush(
        [FromBody] UnregisterPushDeviceRequest? request, CancellationToken ct)
        => UnregisterCore(request, ct);

    /// <summary>Same as DELETE; prefer this when proxies strip DELETE bodies.</summary>
    [HttpPost("push/unregister")]
    public Task<IActionResult> UnregisterPushPost(
        [FromBody] UnregisterPushDeviceRequest? request, CancellationToken ct)
        => UnregisterCore(request, ct);

    private async Task<IActionResult> UnregisterCore(
        UnregisterPushDeviceRequest? request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        await pushDevices.UnregisterAsync(userId, request, ct);
        return Ok(ApiResponse.Ok<object?>(null, "Push device unregistered"));
    }

    [HttpGet("push")]
    public async Task<IActionResult> ListPush(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var devices = await pushDevices.ListAsync(userId, ct);
        return Ok(devices);
    }
}
