namespace Callu.Shared.Models.Webhooks;

/// <summary>
/// Service webhook settings
/// </summary>
public record ServiceWebhookSettingsDto
{
    public Guid ServiceId { get; init; }
    public string? ProviderId { get; init; }
    public string? ProviderName { get; init; }
    public bool WebhookEnabled { get; init; }
    public string? WebhookUrl { get; init; }
    public string? WebhookToken { get; init; }
    public string? ApiKey { get; init; }
    public bool HasApiKey { get; init; }
    public bool ListeningMode { get; init; }
    /// <summary>True when an HMAC signing secret is configured. The secret
    /// value is never returned by GETs — admins copy it once at set time.</summary>
    public bool HasSignatureSecret { get; init; }
    /// <summary>Header name the signature is delivered under (default X-Callu-Signature).</summary>
    public string? SignatureHeaderName { get; init; }
    public Guid? TemplateId { get; init; }
    public string? TemplateName { get; init; }
    public DateTime? LastWebhookReceivedAt { get; init; }
    public int WebhooksReceivedCount { get; init; }
    public int CapturedCount { get; init; }
}
