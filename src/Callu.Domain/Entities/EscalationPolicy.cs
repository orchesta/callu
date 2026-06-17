using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents an escalation policy
/// </summary>
public class EscalationPolicy : BaseEntity
{

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

    /// <summary>
    /// Escalation steps
    /// </summary>
    public virtual ICollection<EscalationStep> Steps { get; set; } = new List<EscalationStep>();
}
