namespace Callu.Shared.Models.Webhooks;

/// <summary>One read-only row in the API-keys overview list; keys are created from the service detail page.</summary>
public record WebhookApiKeyOverviewDto
{
    public Guid ServiceId { get; init; }
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>True when the service has a webhook API key set.</summary>
    public bool HasApiKey { get; init; }

    /// <summary>Last-4-char preview of the key, masked as on the service detail page; null when no key.</summary>
    public string? MaskedApiKey { get; init; }

    /// <summary>True when the service also has an HMAC signature secret configured.</summary>
    public bool HasSignatureSecret { get; init; }

    /// <summary>Most recent regenerate/save timestamp — proxy for "key age".</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>True when the webhook endpoint itself is enabled.</summary>
    public bool WebhookEnabled { get; init; }
}
