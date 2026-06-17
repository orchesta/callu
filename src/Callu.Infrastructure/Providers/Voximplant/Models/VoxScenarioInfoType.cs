using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxScenarioInfoType
{
    [JsonPropertyName("scenario_id")]
    public long ScenarioId { get; set; }
    
    [JsonPropertyName("scenario_name")]
    public string ScenarioName { get; set; } = string.Empty;
    
    [JsonPropertyName("scenario_script")]
    public string? ScenarioScript { get; set; }
    
    [JsonPropertyName("modified")]
    public string? Modified { get; set; }
}
