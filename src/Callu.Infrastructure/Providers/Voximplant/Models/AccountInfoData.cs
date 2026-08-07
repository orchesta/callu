using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class AccountInfoData
{
    [JsonPropertyName("account_name")]
    public string? AccountName { get; set; }
    
    [JsonPropertyName("account_email")]
    public string? AccountEmail { get; set; }
    
    [JsonPropertyName("live_balance")]
    public decimal LiveBalance { get; set; }
    
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
    
    [JsonPropertyName("active")]
    public bool Active { get; set; }
    
    [JsonPropertyName("account_id")]
    public long AccountId { get; set; }
}
