using System.ComponentModel.DataAnnotations;

namespace Callu.Domain.Entities;

/// <summary>Junction row pairing an escalation step with a specific Identity user to notify.</summary>
public class EscalationStepUser
{
    public Guid EscalationStepId { get; set; }

    public virtual EscalationStep EscalationStep { get; set; } = null!;

    [Required]
    [StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
