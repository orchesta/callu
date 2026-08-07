using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Models.Integrations;

namespace Callu.Api.Controllers;

/// <summary>
/// Inbound integrations — named webhook endpoints that feed a service and stamp SourceIntegrationId.
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/integrations")]
[Authorize(Policy = Policies.CanManageIntegrations)]
public class IntegrationsController(IIntegrationService integrations) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? serviceId, CancellationToken ct)
    {
        var items = await integrations.GetAllAsync(serviceId, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var item = await integrations.GetByIdAsync(id, ct);
        if (item is null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateIntegrationRequest request, CancellationToken ct)
    {
        var secrets = await integrations.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = secrets.Id }, secrets);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIntegrationRequest request, CancellationToken ct)
    {
        var item = await integrations.UpdateAsync(id, request, ct);
        return Ok(item);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await integrations.DeleteAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    /// <summary>Mints a new token and API key; previous credentials stop working immediately.</summary>
    [HttpPost("{id:guid}/rotate-credentials")]
    public async Task<IActionResult> RotateCredentials(Guid id, CancellationToken ct)
    {
        var secrets = await integrations.RotateCredentialsAsync(id, ct);
        return Ok(secrets);
    }
}
