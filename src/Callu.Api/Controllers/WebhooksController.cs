using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Callu.Application.Services;
using Callu.Shared.Results;
using Microsoft.AspNetCore.Authorization;

namespace Callu.Api.Controllers;

/// <summary>
/// Webhook endpoints for external integrations
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/webhooks")]
[EnableRateLimiting("webhook")]
[AllowAnonymous]
public class WebhooksController(
    IWebhookProcessingService webhookService,
    ILogger<WebhooksController> logger)
    : ControllerBase
{
    // Header wins over ?apiKey=, which proxies write to their access log.
    private const string ApiKeyHeaderName = "X-Callu-Api-Key";
    private const string AlternateApiKeyHeaderName = "X-Api-Key";

    /// <summary>
    /// Receive webhook using service token (Webhook Sniffer system)
    /// </summary>
    [HttpPost("{token}")]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> ReceiveWebhookByToken(
        string token,
        [FromQuery] string? apiKey = null)
    {
        var presentedApiKey = ReadHeaderApiKey() ?? apiKey;

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var headers = Request.Headers
            .ToDictionary(h => h.Key, h => h.Value.FirstOrDefault() ?? "");
        var contentType = Request.ContentType ?? "application/json";
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        logger.LogDebug("Webhook received: Token={Token}, Method={Method}, ContentType={ContentType}, BodyLength={BodyLength}",
            token, Request.Method, contentType, body.Length);

        var result = await webhookService.ProcessWebhookAsync(
            token, presentedApiKey, Request.Method, contentType, body, headers, sourceIp);

        if (!result.Success)
        {
            logger.LogWarning("Webhook processing failed: {Message}", result.Message);
            return BadRequest(ApiResponse.Fail(result.Message ?? "Webhook processing failed"));
        }

        return Ok(result);
    }

    private string? ReadHeaderApiKey() =>
        FirstNonEmptyHeaderValue(ApiKeyHeaderName) ?? FirstNonEmptyHeaderValue(AlternateApiKeyHeaderName);

    private string? FirstNonEmptyHeaderValue(string name) =>
        Request.Headers.TryGetValue(name, out var values)
            ? values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
            : null;
}
