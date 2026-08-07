using System.Text.Json;
using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>Configuration for the callu-voice provider; property names must match the camelCase keys
/// the frontend writes, because deserialization here is case-sensitive.</summary>
public class CalluVoiceConfig
{
    /// <summary>Base URL of the callu-voice service, e.g. "http://callu-voice:8090".</summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Bearer token callu-voice requires on POST /calls.</summary>
    [JsonPropertyName("apiToken")]
    public string? ApiToken { get; set; }

    /// <summary>The address callu-voice can reach this installation on: scheme, host and port, nothing after them.</summary>
    // Only the address, because the path is not the operator's to guess — Callu answers on one route and
    // appends it, so a callback cannot be configured onto a URL that 404s.
    [JsonPropertyName("callbackUrl")]
    public string? CallbackUrl { get; set; }

    /// <summary>The config key <see cref="CallbackUrl"/> is stored under.</summary>
    public const string CallbackUrlKey = "callbackUrl";

    /// <summary>The path this installation answers callu-voice call status on; Callu appends it to <see cref="CallbackUrl"/> itself.</summary>
    public const string CallbackPath = "/api/callu-voice/callback";

    /// <summary>Why a provider carrying this callback URL cannot be enabled, or null when it can.</summary>
    // With no callback the service reports nothing back: the responder hears the incident acknowledged
    // while it stays open, no call log is written, and nothing arms a further attempt.
    public static string? RefuseCallbackUrl(string? callbackUrl)
    {
        if (string.IsNullOrWhiteSpace(callbackUrl))
            return "callu-voice cannot be enabled without 'callbackUrl'. Without it the service never reports "
                   + "what the responder pressed: they hear the incident acknowledged while it stays open, and "
                   + "nothing arms another attempt. Set 'callbackUrl' to the address callu-voice can reach Callu "
                   + "on — scheme, host and port, and not localhost — then enable the provider. Callu appends "
                   + $"'{CallbackPath}' and the call's own token to it.";

        var trimmed = callbackUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
            return $"'{trimmed}' is not an address callu-voice can post to. 'callbackUrl' must be an absolute "
                   + "http or https URL naming a host this installation answers on; anything else means no call "
                   + "status ever arrives and every page looks answered when nobody has taken it.";

        // The value is deliberately not quoted back: this is the branch a URL carrying credentials
        // lands in, and the refusal is logged.
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return "'callbackUrl' carries more than an address. It is the scheme, host and port only — "
                   + $"Callu appends '{CallbackPath}' and the call's own token to it, and a query string, a "
                   + "fragment or credentials already on the URL would be dropped when it does.";

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length > 0 && !string.Equals(path, CallbackPath, StringComparison.OrdinalIgnoreCase))
            return $"'{trimmed}' ends in a path this installation does not answer on. Callu serves callu-voice "
                   + $"call status on '{CallbackPath}' and nowhere else, so a status posted to '{uri.AbsolutePath}' "
                   + "is a 404: the responder hears the incident acknowledged and it stays open. Give 'callbackUrl' "
                   + "the address alone and Callu appends the path itself.";

        return null;
    }

    /// <summary>The config key <see cref="BaseUrl"/> is stored under.</summary>
    public const string BaseUrlKey = "baseUrl";

    /// <summary>Identifies the voice service a base URL names, so two providers naming one service compare equal.</summary>
    // A voice service holds one carrier, so this is the key a second owner of it is found by.
    public static string? VoiceServiceKey(string? baseUrl)
    {
        var trimmed = baseUrl?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }

    /// <summary>The same key, read out of a provider row's stored config.</summary>
    public static string? VoiceServiceKeyFromJson(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(configJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(BaseUrlKey, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? VoiceServiceKey(value.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Synthesizer voice name; empty leaves the choice to callu-voice's own default.</summary>
    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    /// <summary>How long to wait for POST /calls.</summary>
    // The service renders every prompt before it answers, so this is a synthesis wait, not a network one.
    [JsonPropertyName("requestTimeoutSeconds")]
    public int? RequestTimeoutSeconds { get; set; }
}
