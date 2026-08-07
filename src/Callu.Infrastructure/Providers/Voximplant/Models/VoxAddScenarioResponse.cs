using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxAddScenarioResponse : VoxBaseResponse
{
    [JsonPropertyName("scenario_id")]
    public long ScenarioId { get; set; }
}
