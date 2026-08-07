using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Models.Escalations;

public record UpdateStepRequest
{
    [StringLength(100)]
    public string? Title { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    public int? DelayMinutes { get; init; }

    public Guid? ScheduleId { get; init; }

    public Guid? TeamId { get; init; }

    public bool? NotifyAllTeamMembers { get; init; }

    /// <summary>When the step targets a schedule, page the secondary on-call as well as the primary. Null = unchanged.</summary>
    public bool? NotifyBothOnCall { get; init; }

    public IEnumerable<string>? NotifyUserIds { get; init; }
}
