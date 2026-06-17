using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Models.Services;
using Callu.Shared.Results;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/services")]
[Authorize]
public class ServicesController(
    IServiceManagementService serviceManagement,
    INotificationPushService pushService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.CanViewServices)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await serviceManagement.GetAllAsync(page, pageSize, ct);
        if (!result.IsSuccess) return BadRequest(ApiResponse.Fail(result.Error ?? "Operation failed"));
        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewServices)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await serviceManagement.GetByIdAsync(id, ct);
        if (!result.IsSuccess) return NotFound(ApiResponse.Fail(result.Error ?? "Not found"));
        return Ok(result.Value);
    }

    [HttpPost]
    [Authorize(Policy = Policies.CanManageServices)]
    public async Task<IActionResult> Create([FromBody] CreateServiceRequest request, CancellationToken ct)
    {
        var result = await serviceManagement.CreateAsync(request, ct);
        if (!result.IsSuccess) return BadRequest(ApiResponse.Fail(result.Error ?? "Operation failed"));
        await pushService.BroadcastServiceUpdatedAsync(result.Value!.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageServices)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServiceRequest request, CancellationToken ct)
    {
        var result = await serviceManagement.UpdateAsync(id, request, ct);
        if (!result.IsSuccess) return BadRequest(ApiResponse.Fail(result.Error ?? "Operation failed"));
        await pushService.BroadcastServiceUpdatedAsync(id, ct);
        return Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.CanManageServices)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await serviceManagement.DeleteAsync(id, ct);
        if (!result.IsSuccess) return NotFound(ApiResponse.Fail(result.Error ?? "Not found"));
        await pushService.BroadcastServiceUpdatedAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/dependencies")]
    [Authorize(Policy = Policies.CanViewServices)]
    public async Task<IActionResult> GetDependencies(Guid id, CancellationToken ct)
    {
        var result = await serviceManagement.GetDependenciesAsync(id, ct);
        if (!result.IsSuccess) return BadRequest(ApiResponse.Fail(result.Error ?? "Operation failed"));
        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/dependencies")]
    [Authorize(Policy = Policies.CanManageServices)]
    public async Task<IActionResult> AddDependency(Guid id, [FromBody] CreateServiceDependencyRequest request, CancellationToken ct)
    {
        var result = await serviceManagement.AddDependencyAsync(id, request, ct);
        if (!result.IsSuccess) return BadRequest(ApiResponse.Fail(result.Error ?? "Operation failed"));
        return Ok(result.Value);
    }

    [HttpDelete("dependencies/{dependencyId:guid}")]
    [Authorize(Policy = Policies.CanManageServices)]
    public async Task<IActionResult> RemoveDependency(Guid dependencyId, CancellationToken ct)
    {
        var result = await serviceManagement.RemoveDependencyAsync(dependencyId, ct);
        if (!result.IsSuccess) return NotFound(ApiResponse.Fail(result.Error ?? "Not found"));
        return NoContent();
    }
}
