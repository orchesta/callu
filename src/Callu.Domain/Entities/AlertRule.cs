using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents an automation rule that triggers actions when incidents match specified conditions.
/// Rules are evaluated in priority order when incidents are created or updated.
/// </summary>
public class AlertRule : BaseEntity
{
    /// <summary>
    /// Rule name for identification
    /// </summary>
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this rule does
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this rule is currently active
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Evaluation priority — lower numbers are evaluated first
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// JSON-serialized array of conditions that must ALL match (AND logic).
    /// Format: [{"field":"Severity","operator":"Equals","value":"Critical"}, ...]
    /// </summary>
    public string ConditionsJson { get; set; } = "[]";

    /// <summary>
    /// JSON-serialized array of actions to execute when conditions match.
    /// Format: [{"type":"AutoEscalate","target":"team-guid"}, ...]
    /// </summary>
    public string ActionsJson { get; set; } = "[]";

    /// <summary>
    /// Optional: scope this rule to a specific service only
    /// </summary>
    public Guid? ServiceId { get; set; }

    /// <summary>
    /// Navigation property for scoped service
    /// </summary>
    public virtual Service? Service { get; set; }

    /// <summary>
    /// Optional: scope this rule to a specific team only
    /// </summary>
    public Guid? TeamId { get; set; }

    /// <summary>
    /// Navigation property for scoped team
    /// </summary>
    public virtual Team? Team { get; set; }

    /// <summary>
    /// How many times this rule has been triggered
    /// </summary>
    public int TriggerCount { get; set; } = 0;

    /// <summary>
    /// When this rule was last triggered
    /// </summary>
    public DateTime? LastTriggeredAt { get; set; }
}
