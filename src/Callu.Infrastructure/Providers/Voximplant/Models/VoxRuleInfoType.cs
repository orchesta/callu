using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxRuleInfoType
{
    [JsonPropertyName("rule_id")]
    public long RuleId { get; set; }
    
    [JsonPropertyName("rule_name")]
    public string RuleName { get; set; } = string.Empty;
    
    [JsonPropertyName("rule_pattern")]
    public string RulePattern { get; set; } = string.Empty;
    
    [JsonPropertyName("rule_pattern_exclude")]
    public string? RulePatternExclude { get; set; }
    
    [JsonPropertyName("video_conference")]
    public bool VideoConference { get; set; }
    
    [JsonPropertyName("modified")]
    public string? Modified { get; set; }
    
    [JsonPropertyName("scenarios")]
    public List<VoxScenarioInfoType>? Scenarios { get; set; }
}
