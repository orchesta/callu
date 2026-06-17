using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Models.Escalations;

public record CreateEscalationStepRequest
{
    public int Level { get; init; }

    [Required]
    [StringLength(100)]
    public string Title { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public int DelayMinutes { get; init; }

    public Guid? ScheduleId { get; init; }

    public Guid? TeamId { get; init; }

    public bool NotifyAllTeamMembers { get; init; } = false;

    public IEnumerable<string>? NotifyUserIds { get; init; }
}
