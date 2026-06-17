using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxApplicationInfoType
{
    [JsonPropertyName("application_id")]
    public long ApplicationId { get; set; }
    
    [JsonPropertyName("application_name")]
    public string ApplicationName { get; set; } = string.Empty;
    
    [JsonPropertyName("modified")]
    public string? Modified { get; set; }
    
    [JsonPropertyName("secure_record_storage")]
    public bool SecureRecordStorage { get; set; }
}
