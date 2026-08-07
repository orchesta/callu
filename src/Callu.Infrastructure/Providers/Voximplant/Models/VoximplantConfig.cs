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

    /// <summary>Voximplant data-center node the account lives on (NODE_1…NODE_12), required by the Web SDK.</summary>
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

    /// <summary>Optional service-account JSON; when set, the Management API uses JWT Bearer auth instead
    /// of putting <see cref="ApiKey"/> in the query string.</summary>
    [JsonPropertyName("serviceAccountJson")]
    public string? ServiceAccountJson { get; set; }

}
