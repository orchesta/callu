using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

internal class ProvisioningConfig
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long ApplicationId { get; set; }
    
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long IncidentCallScenarioId { get; set; }
    
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long ConferenceScenarioId { get; set; }
    
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long IncidentCallRuleId { get; set; }
    
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long ConferenceRuleId { get; set; }
    
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long SystemUserId { get; set; }
    public string ScenarioApiKey { get; set; } = string.Empty;
    public DateTime? LastProvisionedAt { get; set; }
}
