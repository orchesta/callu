using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Models.Email;
using Callu.Shared.Results;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/email-templates")]
[Authorize(Policy = Policies.CanManageSettings)]
public class EmailTemplatesController(IEmailTemplateService emailTemplateService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var templates = await emailTemplateService.GetAllAsync(ct);
        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var template = await emailTemplateService.GetByIdAsync(id, ct);
        if (template == null) return NotFound();
        return Ok(template);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmailTemplateRequest request, CancellationToken ct)
    {
        var template = await emailTemplateService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = template.Id }, template);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEmailTemplateRequest request, CancellationToken ct)
    {
        var template = await emailTemplateService.UpdateAsync(id, request, ct);
        if (template == null) return NotFound();
        return Ok(template);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await emailTemplateService.DeleteAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, [FromBody] PreviewEmailTemplateRequest request, CancellationToken ct)
    {
        var html = await emailTemplateService.PreviewAsync(id, request.Variables, ct);
        return Ok(new { html });
    }

    [HttpPost("{id:guid}/send-test")]
    public async Task<IActionResult> SendTest(Guid id, [FromBody] SendTestEmailRequest request, CancellationToken ct)
    {
        var (success, error) = await emailTemplateService.SendTestAsync(id, request.Email, ct);
        if (!success) return BadRequest(ApiResponse.Fail(error ?? Messages.Get("emailTemplates.testFailed")));
        return Ok(new { message = Messages.Get("emailTemplates.testSent") });
    }
}

