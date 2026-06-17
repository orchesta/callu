using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

/// <summary>
/// Voximplant provider configuration
/// </summary>
public class VoximplantConfig
{
    [JsonPropertyName("accountId")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long AccountId { get; set; }
    
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;
    
    [JsonPropertyName("applicationName")]
    public string ApplicationName { get; set; } = string.Empty;
    
    [JsonPropertyName("accountName")]
    public string AccountName { get; set; } = string.Empty;

    /// <summary>
    /// Voximplant data-center node the account lives on (NODE_1…NODE_12). Required by the
    /// Web SDK to connect for video conferences; chosen in the provider form. Found in the
    /// Voximplant control panel → "Credentials for working with API, SDK, SIP".
    /// </summary>
    [JsonPropertyName("node")]
    public string Node { get; set; } = string.Empty;

    [JsonPropertyName("incidentCallRuleId")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? IncidentCallRuleId { get; set; }
    
    [JsonPropertyName("conferenceRuleId")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? ConferenceRuleId { get; set; }
    
    [JsonPropertyName("callbackBaseUrl")]
    public string? CallbackBaseUrl { get; set; }
    
    [JsonPropertyName("defaultCallerId")]
    public string? DefaultCallerId { get; set; }

    /// <summary>
    /// Optional Voximplant Service Account JSON (generated in Voximplant Control Panel →
    /// Account → Service Accounts). When set, the Management API is called with JWT Bearer
    /// auth instead of sending <see cref="ApiKey"/> in the URL query string, so the key is
    /// never logged by proxies. Contents must include <c>account_id</c>, <c>key_id</c>, and
    /// <c>private_key</c> (PEM-encoded RSA).
    /// </summary>
    [JsonPropertyName("serviceAccountJson")]
    public string? ServiceAccountJson { get; set; }

}
