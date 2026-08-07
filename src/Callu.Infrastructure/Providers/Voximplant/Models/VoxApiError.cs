using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxApiError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
    
    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;
}
