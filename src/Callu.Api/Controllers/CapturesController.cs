using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Localization;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/captures")]
[Authorize(Policy = Policies.CanManageWebhooks)]
public class CapturesController(IWebhookCaptureService captureService) : ControllerBase
{
    [HttpGet("service/{serviceId:guid}")]
    public async Task<IActionResult> GetByService(Guid serviceId, CancellationToken ct)
    {
        var captures = await captureService.GetCapturesAsync(serviceId, ct);
        return Ok(captures);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var capture = await captureService.GetCaptureByIdAsync(id, ct);
        if (capture == null) return NotFound();
        return Ok(capture);
    }

    [HttpPost("{id:guid}/review")]
    public async Task<IActionResult> MarkAsReviewed(Guid id, CancellationToken ct)
    {
        var success = await captureService.MarkAsReviewedAsync(id, ct);
        if (!success) return NotFound();
        return Ok(new { message = Messages.Get("captures.reviewed") });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await captureService.DeleteCaptureAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpDelete("service/{serviceId:guid}")]
    public async Task<IActionResult> DeleteAll(Guid serviceId, CancellationToken ct)
    {
        var count = await captureService.DeleteAllCapturesAsync(serviceId, ct);
        return Ok(new { deletedCount = count });
    }

    [HttpGet("integration/{integrationId:guid}")]
    public async Task<IActionResult> GetByIntegration(Guid integrationId, CancellationToken ct)
    {
        var captures = await captureService.GetCapturesByIntegrationAsync(integrationId, ct);
        return Ok(captures);
    }

    [HttpDelete("integration/{integrationId:guid}")]
    public async Task<IActionResult> DeleteAllByIntegration(Guid integrationId, CancellationToken ct)
    {
        var count = await captureService.DeleteAllCapturesByIntegrationAsync(integrationId, ct);
        return Ok(new { deletedCount = count });
    }
}
