using Callu.Shared.Models.Webhooks;

namespace Callu.Application.Services;

/// <summary>
/// Manages webhook configuration — provider setup, token/key management, listening mode, templates.
/// </summary>
public interface IWebhookConfigService
{
    /// <summary>
    /// Set provider for a service (main method for provider selection).
    /// </summary>
    Task<ServiceWebhookSettingsDto> SetProviderAsync(Guid serviceId, string providerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disable webhook receiving for a service.
    /// </summary>
    Task<bool> DisableWebhookAsync(Guid serviceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Regenerate webhook token for a service.
    /// </summary>
    Task<string> RegenerateTokenAsync(Guid serviceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Regenerate API key for a service.
    /// </summary>
    Task<string> RegenerateApiKeyAsync(Guid serviceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Enable listening/capture mode.
    /// </summary>
    Task<bool> EnableListeningModeAsync(Guid serviceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disable listening/capture mode.
    /// </summary>
    Task<bool> DisableListeningModeAsync(Guid serviceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Set webhook template for a service.
    /// </summary>
    Task<bool> SetTemplateAsync(Guid serviceId, Guid? templateId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get webhook settings for a service.
    /// </summary>
    Task<ServiceWebhookSettingsDto?> GetWebhookSettingsAsync(Guid serviceId, bool includeToken = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the HMAC signature secret and header name; the plaintext is returned only on this call.
    /// </summary>
    Task<bool> SetSignatureAsync(Guid serviceId, string secret, string? headerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear the HMAC signature secret and header name, leaving webhooks unsigned.
    /// </summary>
    Task<bool> ClearSignatureAsync(Guid serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only inventory of webhook API keys across all services, masked to the last 4 characters.
    /// </summary>
    Task<IReadOnlyList<WebhookApiKeyOverviewDto>> ListWebhookApiKeysAsync(CancellationToken cancellationToken = default);
}
