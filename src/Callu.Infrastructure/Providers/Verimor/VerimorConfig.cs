using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Verimor;

/// <summary>
/// Verimor provider configuration
/// </summary>
public class VerimorConfig
{
    [JsonPropertyName("apiUsername")]
    public string ApiUsername { get; set; } = string.Empty;
    
    [JsonPropertyName("apiPassword")]
    public string ApiPassword { get; set; } = string.Empty;
    
    [JsonPropertyName("senderId")]
    public string SenderId { get; set; } = string.Empty;
}
