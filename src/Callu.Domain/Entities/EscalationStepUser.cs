using System.ComponentModel.DataAnnotations;

namespace Callu.Domain.Entities;

/// <summary>
/// Junction row pairing an EscalationStep with a specific Identity user to notify.
/// Replaces the legacy EscalationStep.NotifyUserIds comma-separated string so that
/// a deleted user cascades out of escalation targets (no orphaned GUIDs silently
/// suppressing a page) and so the set can be audited and indexed normally.
/// </summary>
public class EscalationStepUser
{
    public Guid EscalationStepId { get; set; }

    public virtual EscalationStep EscalationStep { get; set; } = null!;

    [Required]
    [StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
