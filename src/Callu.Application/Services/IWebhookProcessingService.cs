using Callu.Shared.Models.Webhooks;

namespace Callu.Application.Services;

/// <summary>
/// Handles incoming webhook processing — ingestion, template parsing, incident creation/resolution.
/// </summary>
public interface IWebhookProcessingService
{
    /// <summary>
    /// Process an incoming webhook request using the service token (Webhook Sniffer).
    /// </summary>
    Task<WebhookProcessResult> ProcessWebhookAsync(
        string token, 
        string? apiKey,
        string method,
        string? contentType,
        string body,
        IDictionary<string, string> headers,
        string? sourceIp,
        CancellationToken cancellationToken = default);
}
