using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents a step in an escalation policy
/// </summary>
public class EscalationStep : BaseEntity
{
    /// <summary>
    /// Escalation policy ID
    /// </summary>
    public Guid EscalationPolicyId { get; set; }

    /// <summary>
    /// Navigation property for policy
    /// </summary>
    public virtual EscalationPolicy EscalationPolicy { get; set; } = null!;

    /// <summary>
    /// Step level (1, 2, 3, etc.)
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Step title
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Step description
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Delay in minutes before escalating to this step
    /// </summary>
    public int DelayMinutes { get; set; }

    /// <summary>
    /// Schedule ID to notify (optional)
    /// </summary>
    public Guid? ScheduleId { get; set; }

    /// <summary>
    /// Navigation property for schedule
    /// </summary>
    public virtual Schedule? Schedule { get; set; }

    /// <summary>
    /// Team ID to notify (optional)
    /// </summary>
    public Guid? TeamId { get; set; }

    /// <summary>
    /// Navigation property for team
    /// </summary>
    public virtual Team? Team { get; set; }

    /// <summary>
    /// Whether to notify all team members or just the on-call member
    /// </summary>
    public bool NotifyAllTeamMembers { get; set; } = false;

    /// <summary>
    /// Whether to page both the primary AND the secondary on-call slot on
    /// this step. No-op for schedules that don't define a secondary. Default
    /// off preserves "primary only" behaviour. Fix 05.F11.
    /// </summary>
    public bool NotifyBothOnCall { get; set; } = false;

    /// <summary>
    /// Junction-backed user targets — source of truth for "users to notify on this step".
    /// </summary>
    public virtual ICollection<EscalationStepUser> TargetedUsers { get; set; } = new List<EscalationStepUser>();
}
