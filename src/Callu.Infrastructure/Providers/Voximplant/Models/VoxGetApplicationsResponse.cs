using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxGetApplicationsResponse : VoxBaseResponse
{
    [JsonPropertyName("result")]
    public new List<VoxApplicationInfoType>? Result { get; set; }
    
    [JsonPropertyName("count")]
    public int Count { get; set; }
    
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }
}
