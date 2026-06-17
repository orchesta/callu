using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxAddRuleResponse : VoxBaseResponse
{
    [JsonPropertyName("rule_id")]
    public long RuleId { get; set; }
}
