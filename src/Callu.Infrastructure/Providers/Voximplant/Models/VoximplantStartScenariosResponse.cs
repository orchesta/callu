using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

internal class VoximplantStartScenariosResponse : VoximplantApiResponse
{
    [JsonPropertyName("result")]
    public int Result { get; set; }
    
    [JsonPropertyName("media_session_access_url")]
    public string? MediaSessionAccessUrl { get; set; }
}
