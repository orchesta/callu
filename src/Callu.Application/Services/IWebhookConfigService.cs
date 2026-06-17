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
    Task<ServiceWebhookSettingsDto?> GetWebhookSettingsAsync(Guid serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the HMAC signature secret + header name on the service. Used both
    /// to verify inbound webhook bodies and to sign outbound ACK callbacks
    /// (same shared secret, symmetric trust). The plaintext secret is only
    /// returned to the caller on this set — subsequent GETs only return a
    /// HasSignatureSecret flag. Fix 10.P1-6.
    /// </summary>
    Task<bool> SetSignatureAsync(Guid serviceId, string secret, string? headerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear the HMAC signature secret + header name on the service. After
    /// this, inbound webhooks no longer require a signature and outbound
    /// ACK callbacks are sent unsigned. Fix 10.P1-6.
    /// </summary>
    Task<bool> ClearSignatureAsync(Guid serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only inventory of webhook API keys across all services. Powers the
    /// /settings/api-keys page — admins want to see which services have a key
    /// at a glance without paging through every service detail. Keys are masked
    /// (last-4 only) because the plaintext was returned exactly once on regenerate.
    /// </summary>
    Task<IReadOnlyList<WebhookApiKeyOverviewDto>> ListWebhookApiKeysAsync(CancellationToken cancellationToken = default);
}
