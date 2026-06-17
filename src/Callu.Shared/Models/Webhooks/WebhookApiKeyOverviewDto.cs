namespace Callu.Shared.Models.Webhooks;

/// <summary>
/// One row in the /settings/api-keys overview list. Read-only — keys are
/// created from the service detail page (no central "create" action), and the
/// plaintext is only available there at regenerate time.
/// </summary>
public record WebhookApiKeyOverviewDto
{
    public Guid ServiceId { get; init; }
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>True when the service has a webhook API key set.</summary>
    public bool HasApiKey { get; init; }

    /// <summary>
    /// Last-4-char preview of the key, e.g. "•••• abcd". Null when no key.
    /// Matches the masking used on the service detail page so the same value
    /// shown in both places lines up character-for-character.
    /// </summary>
    public string? MaskedApiKey { get; init; }

    /// <summary>
    /// True when the service also has an HMAC signature secret configured.
    /// Surfaced so the admin can spot services that authenticate but don't
    /// verify body integrity at a glance.
    /// </summary>
    public bool HasSignatureSecret { get; init; }

    /// <summary>Most recent regenerate/save timestamp — proxy for "key age".</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// True when the webhook endpoint itself is enabled. Listed services that
    /// have a key but aren't enabled show a warning badge — the key is dead
    /// weight until the endpoint is turned back on.
    /// </summary>
    public bool WebhookEnabled { get; init; }
}
