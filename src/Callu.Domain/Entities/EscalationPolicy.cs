using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents an escalation policy
/// </summary>
public class EscalationPolicy : BaseEntity
{
    public const int MinRepeatCycles = 1;
    public const int MaxRepeatCyclesCap = 10;
    public const int DefaultMaxRepeatCycles = 3;
    public const int MinRepeatDurationMinutes = 60;
    public const int MaxRepeatDurationMinutesCap = 24 * 60;
    public const int DefaultMaxRepeatDurationMinutes = 4 * 60;

    /// <summary>
    /// Policy name
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Policy description
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Team ID this policy belongs to
    /// </summary>
    public Guid? TeamId { get; set; }

    /// <summary>
    /// Navigation property for team
    /// </summary>
    public virtual Team? Team { get; set; }

    /// <summary>
    /// Is this policy active
    /// </summary>
    public bool IsActive { get; set; } = true;

    public EscalationExhaustionBehavior ExhaustionBehavior { get; set; } = EscalationExhaustionBehavior.Stop;

    public int MaxRepeatCycles { get; set; } = DefaultMaxRepeatCycles;

    public int MaxRepeatDurationMinutes { get; set; } = DefaultMaxRepeatDurationMinutes;

    /// <summary>
    /// Escalation steps
    /// </summary>
    public virtual ICollection<EscalationStep> Steps { get; set; } = new List<EscalationStep>();
}
