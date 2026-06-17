using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Callu.Application.Services;
using Callu.Shared.Results;

namespace Callu.Api.Controllers;

/// <summary>
/// Webhook endpoints for external integrations
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/webhooks")]
[EnableRateLimiting("webhook")]
public class WebhooksController(
    IWebhookProcessingService webhookService,
    ILogger<WebhooksController> logger)
    : ControllerBase
{
    /// <summary>
    /// Receive webhook using service token (Webhook Sniffer system)
    /// </summary>
    [HttpPost("{token}")]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> ReceiveWebhookByToken(
        string token,
        [FromQuery] string? apiKey = null)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var headers = Request.Headers
            .ToDictionary(h => h.Key, h => h.Value.FirstOrDefault() ?? "");
        var contentType = Request.ContentType ?? "application/json";
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        logger.LogDebug("Webhook received: Token={Token}, Method={Method}, ContentType={ContentType}, BodyLength={BodyLength}",
            token, Request.Method, contentType, body.Length);

        var result = await webhookService.ProcessWebhookAsync(
            token, apiKey, Request.Method, contentType, body, headers, sourceIp);

        if (!result.Success)
        {
            logger.LogWarning("Webhook processing failed: {Message}", result.Message);
            return BadRequest(ApiResponse.Fail(result.Message ?? "Webhook processing failed"));
        }

        return Ok(result);
    }
}
