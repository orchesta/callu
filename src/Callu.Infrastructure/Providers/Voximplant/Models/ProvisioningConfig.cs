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

    /// <summary>Contract version of the uploaded VoxEngine script; absent means a script old enough that
    /// it sends no call token, so its acknowledgements are refused.</summary>
    public string? ScriptContractVersion { get; set; }
}
