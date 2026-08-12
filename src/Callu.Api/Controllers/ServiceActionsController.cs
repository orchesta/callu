using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Models.Services;

namespace Callu.Api.Controllers;

/// <summary>
/// Operator-defined outbound actions of a service.
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/services/{serviceId:guid}/actions")]
[Authorize(Policy = Policies.CanViewServices)]
public class ServiceActionsController(IServiceActionService serviceActions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid serviceId, CancellationToken ct)
    {
        // Headers can carry credentials, so only managers get them back.
        var includeSensitive = User.HasClaim("CanManageServices", "true");
        var items = await serviceActions.GetForServiceAsync(serviceId, includeSensitive, ct);
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = Policies.CanManageServices)]
    public async Task<IActionResult> Create(Guid serviceId, [FromBody] CreateServiceActionRequest request, CancellationToken ct)
    {
        var item = await serviceActions.CreateAsync(serviceId, request, ct);
        return CreatedAtAction(nameof(GetAll), new { serviceId }, item);
    }

    [HttpPut("{actionId:guid}")]
    [Authorize(Policy = Policies.CanManageServices)]
    public async Task<IActionResult> Update(Guid serviceId, Guid actionId, [FromBody] UpdateServiceActionRequest request, CancellationToken ct)
    {
        var item = await serviceActions.UpdateAsync(serviceId, actionId, request, ct);
        return Ok(item);
    }

    [HttpDelete("{actionId:guid}")]
    [Authorize(Policy = Policies.CanManageServices)]
    public async Task<IActionResult> Delete(Guid serviceId, Guid actionId, CancellationToken ct)
    {
        await serviceActions.DeleteAsync(serviceId, actionId, ct);
        return NoContent();
    }
}
