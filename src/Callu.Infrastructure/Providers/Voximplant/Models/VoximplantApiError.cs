using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

internal class VoximplantApiError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
    
    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;
}
