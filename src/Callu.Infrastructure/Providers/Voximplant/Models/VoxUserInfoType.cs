using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxUserInfoType
{
    [JsonPropertyName("user_id")]
    public long UserId { get; set; }
    
    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = string.Empty;
    
    [JsonPropertyName("user_display_name")]
    public string UserDisplayName { get; set; } = string.Empty;
    
    [JsonPropertyName("user_active")]
    public bool UserActive { get; set; }
    
    [JsonPropertyName("user_custom_data")]
    public string? UserCustomData { get; set; }
}
