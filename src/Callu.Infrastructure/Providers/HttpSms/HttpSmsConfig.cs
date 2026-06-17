using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.HttpSms;

/// <summary>
/// Configuration for the generic HTTP SMS provider. The operator describes their own SMS
/// gateway as an HTTP request template; placeholders ({to}, {message}, {sender}, {apiKey},
/// {username}, {password}) are substituted at send time with location-aware escaping.
/// Secrets live alongside the template (stored like every other provider's config) and are
/// referenced from headers/body/url via their placeholders.
///
/// NOTE: property names MUST match the camelCase keys the frontend writes —
/// <see cref="BaseCommunicationProvider"/>.GetConfig&lt;T&gt; deserializes case-sensitively.
/// </summary>
public class HttpSmsConfig
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>"POST" (default) or "GET".</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "POST";

    /// <summary>POST body encoding: "json" (default) or "form". Ignored for GET.</summary>
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "json";

    /// <summary>Request headers; values may contain placeholders (e.g. "Bearer {apiKey}").</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>Request body template (POST). Contains placeholders. Empty for GET.</summary>
    [JsonPropertyName("bodyTemplate")]
    public string? BodyTemplate { get; set; }

    [JsonPropertyName("senderId")]
    public string? SenderId { get; set; }

    /// <summary>"status2xx" (default) or "jsonField".</summary>
    [JsonPropertyName("successMode")]
    public string? SuccessMode { get; set; }

    /// <summary>Dotted path into the JSON response (e.g. "status" or "data.code") for successMode=jsonField.</summary>
    [JsonPropertyName("successField")]
    public string? SuccessField { get; set; }

    /// <summary>Expected value at <see cref="SuccessField"/> (case-insensitive string compare).</summary>
    [JsonPropertyName("successValue")]
    public string? SuccessValue { get; set; }

    /// <summary>Optional dotted path to extract the provider message id from the JSON response.</summary>
    [JsonPropertyName("messageIdPath")]
    public string? MessageIdPath { get; set; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }
}
