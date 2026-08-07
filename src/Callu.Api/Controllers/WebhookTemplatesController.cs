using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Models.Webhooks;

namespace Callu.Api.Controllers;

/// <summary>
/// Webhook templates — CRUD and test
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/webhook-templates")]
[Authorize(Policy = Policies.CanManageWebhooks)]
public class WebhookTemplatesController(IWebhookTemplateService templateService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var templates = await templateService.GetTemplatesAsync(cancellationToken);
        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var template = await templateService.GetTemplateByIdAsync(id, cancellationToken);
        if (template == null) return NotFound();
        return Ok(template);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWebhookTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await templateService.CreateTemplateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
    }

    [HttpPost("from-capture/{captureId:guid}")]
    public async Task<IActionResult> CreateFromCapture(Guid captureId, [FromBody] CreateWebhookTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await templateService.CreateTemplateFromCaptureAsync(captureId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWebhookTemplateRequest request, CancellationToken cancellationToken)
    {
        var success = await templateService.UpdateTemplateAsync(id, request, cancellationToken);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var success = await templateService.DeleteTemplateAsync(id, cancellationToken);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id, [FromBody] TestPayloadRequest request, CancellationToken cancellationToken)
    {
        var result = await templateService.TestTemplateAsync(id, request.SamplePayload, cancellationToken);
        return Ok(result);
    }
}

