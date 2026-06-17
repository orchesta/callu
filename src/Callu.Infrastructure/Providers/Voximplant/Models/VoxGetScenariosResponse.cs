using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxGetScenariosResponse : VoxBaseResponse
{
    [JsonPropertyName("result")]
    public new List<VoxScenarioInfoType>? Result { get; set; }
    
    [JsonPropertyName("count")]
    public int Count { get; set; }
    
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}
