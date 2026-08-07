using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxGetRulesResponse : VoxBaseResponse
{
    [JsonPropertyName("result")]
    public new List<VoxRuleInfoType>? Result { get; set; }
    
    [JsonPropertyName("count")]
    public int Count { get; set; }
    
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}
