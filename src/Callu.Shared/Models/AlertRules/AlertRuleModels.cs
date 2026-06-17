using Callu.Domain.Enums;

namespace Callu.Shared.Models.AlertRules;

/// <summary>
/// DTO for alert rule display
/// </summary>
public class AlertRuleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public int Priority { get; set; }
    public List<AlertRuleConditionDto> Conditions { get; set; } = [];
    public List<AlertRuleActionDto> Actions { get; set; } = [];
    public Guid? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    public Guid? TeamId { get; set; }
    public string? TeamName { get; set; }
    public int TriggerCount { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Single condition in an alert rule (all conditions are AND-ed)
/// </summary>
public class AlertRuleConditionDto
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "Equals";
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Single action to execute when an alert rule matches
/// </summary>
public class AlertRuleActionDto
{
    public string Type { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Value { get; set; }
}

/// <summary>
/// Request to create a new alert rule
/// </summary>
public class CreateAlertRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public List<AlertRuleConditionDto> Conditions { get; set; } = [];
    public List<AlertRuleActionDto> Actions { get; set; } = [];
    public Guid? ServiceId { get; set; }
    public Guid? TeamId { get; set; }
}

/// <summary>
/// Request to update an existing alert rule
/// </summary>
public class UpdateAlertRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public List<AlertRuleConditionDto> Conditions { get; set; } = [];
    public List<AlertRuleActionDto> Actions { get; set; } = [];
    public Guid? ServiceId { get; set; }
    public Guid? TeamId { get; set; }
}
