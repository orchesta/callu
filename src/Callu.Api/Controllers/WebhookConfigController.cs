using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Results;
using Callu.Shared.Models.Webhooks;

namespace Callu.Api.Controllers;

/// <summary>
/// Webhook configuration for services — provider, token, API key, listening mode, template.
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/services/{serviceId:guid}/webhook-settings")]
[Authorize(Policy = Policies.CanManageServices)]
public class WebhookConfigController(IWebhookConfigService webhookConfig) : ControllerBase
{
    /// <summary>
    /// Inventory of webhook API keys across every service. Powers the
    /// /settings/api-keys read-only list. Lives on this controller because
    /// webhook keys are owned here, but the route deliberately skips the
    /// {serviceId} prefix — it's a workspace-wide view, not per-service.
    /// </summary>
    [HttpGet("/api/v{version:apiVersion}/webhook-api-keys")]
    [Authorize(Policy = Policies.CanViewServices)]
    public async Task<IActionResult> ListAll(CancellationToken ct)
    {
        var keys = await webhookConfig.ListWebhookApiKeysAsync(ct);
        return Ok(keys);
    }

    /// <summary>
    /// Get webhook settings for a service. Read endpoints intentionally drop to
    /// CanViewServices (the controller default is the higher CanManageServices, which
    /// gates the write actions below) — viewing config shouldn't require manage rights.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Policies.CanViewServices)]
    public async Task<IActionResult> GetSettings(Guid serviceId, CancellationToken ct)
    {
        var settings = await webhookConfig.GetWebhookSettingsAsync(serviceId, ct);
        if (settings == null) return NotFound(ApiResponse.Fail(Messages.Get("webhooks.serviceNotFound")));
        return Ok(settings);
    }

    /// <summary>
    /// Set alert provider for a service (enables webhook receiving).
    /// </summary>
    [HttpPost("provider")]
    public async Task<IActionResult> SetProvider(Guid serviceId, [FromBody] SetProviderRequest request, CancellationToken ct)
    {
        var settings = await webhookConfig.SetProviderAsync(serviceId, request.ProviderId, ct);
        return Ok(settings);
    }

    /// <summary>
    /// Disable webhook receiving for a service.
    /// </summary>
    [HttpDelete("provider")]
    public async Task<IActionResult> DisableWebhook(Guid serviceId, CancellationToken ct)
    {
        var success = await webhookConfig.DisableWebhookAsync(serviceId, ct);
        if (!success) return NotFound(ApiResponse.Fail(Messages.Get("webhooks.serviceNotFound")));
        return NoContent();
    }

    /// <summary>
    /// Regenerate webhook token (changes the webhook URL).
    /// </summary>
    [HttpPost("regenerate-token")]
    public async Task<IActionResult> RegenerateToken(Guid serviceId, CancellationToken ct)
    {
        var newToken = await webhookConfig.RegenerateTokenAsync(serviceId, ct);
        return Ok(new { token = newToken });
    }

    /// <summary>
    /// Regenerate API key for webhook authentication.
    /// </summary>
    [HttpPost("regenerate-api-key")]
    public async Task<IActionResult> RegenerateApiKey(Guid serviceId, CancellationToken ct)
    {
        var newKey = await webhookConfig.RegenerateApiKeyAsync(serviceId, ct);
        return Ok(new { apiKey = newKey });
    }

    /// <summary>
    /// Set the HMAC signature secret + header name for this service. The secret
    /// is returned in the response exactly once; subsequent GETs only carry the
    /// HasSignatureSecret flag. Validates a minimum 32-char length and header
    /// name shape on the service side. Fix 10.P1-6.
    /// </summary>
    [HttpPost("signature")]
    public async Task<IActionResult> SetSignature(Guid serviceId, [FromBody] SetSignatureRequest request, CancellationToken ct)
    {
        var ok = await webhookConfig.SetSignatureAsync(serviceId, request.Secret, request.HeaderName, ct);
        if (!ok)
            return BadRequest(ApiResponse.Fail("Secret must be at least 32 characters and the header name must be a valid HTTP token."));
        return Ok(new { secret = request.Secret, headerName = request.HeaderName ?? "X-Callu-Signature" });
    }

    /// <summary>
    /// Remove the HMAC signature secret. Inbound webhooks no longer require a
    /// signature; outbound ACKs go unsigned. Fix 10.P1-6.
    /// </summary>
    [HttpDelete("signature")]
    public async Task<IActionResult> ClearSignature(Guid serviceId, CancellationToken ct)
    {
        var ok = await webhookConfig.ClearSignatureAsync(serviceId, ct);
        if (!ok)
            return NotFound(ApiResponse.Fail(Messages.Get("webhooks.serviceNotFound")));
        return NoContent();
    }

    /// <summary>
    /// Toggle listening/capture mode.
    /// </summary>
    [HttpPost("listening-mode")]
    public async Task<IActionResult> ToggleListeningMode(Guid serviceId, [FromBody] ToggleListeningModeRequest request, CancellationToken ct)
    {
        bool success;
        if (request.Enabled)
            success = await webhookConfig.EnableListeningModeAsync(serviceId, ct);
        else
            success = await webhookConfig.DisableListeningModeAsync(serviceId, ct);

        if (!success) return NotFound(ApiResponse.Fail(Messages.Get("webhooks.serviceNotFound")));
        return Ok(new { listeningMode = request.Enabled });
    }

    /// <summary>
    /// Set the active webhook template for a service.
    /// </summary>
    [HttpPut("template")]
    public async Task<IActionResult> SetTemplate(Guid serviceId, [FromBody] SetTemplateRequest request, CancellationToken ct)
    {
        var success = await webhookConfig.SetTemplateAsync(serviceId, request.TemplateId, ct);
        if (!success) return NotFound(ApiResponse.Fail(Messages.Get("webhooks.serviceNotFound")));
        return Ok(new { templateId = request.TemplateId });
    }
}

