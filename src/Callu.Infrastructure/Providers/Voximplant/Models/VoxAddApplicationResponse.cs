using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxAddApplicationResponse : VoxBaseResponse
{
    [JsonPropertyName("application_id")]
    public long ApplicationId { get; set; }
    
    [JsonPropertyName("application_name")]
    public string? ApplicationName { get; set; }
}
