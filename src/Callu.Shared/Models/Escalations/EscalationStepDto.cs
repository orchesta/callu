namespace Callu.Shared.Models.Escalations;

/// <summary>
/// Escalation policy step DTO
/// </summary>
public record EscalationStepDto
{
    public Guid Id { get; init; }
    public Guid EscalationPolicyId { get; init; }
    public int Level { get; set; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int DelayMinutes { get; init; }
    public Guid? ScheduleId { get; init; }
    public string? ScheduleName { get; init; }
    public Guid? TeamId { get; init; }
    public string? TeamName { get; init; }
    public bool NotifyAllTeamMembers { get; init; }
    public IEnumerable<string> NotifyUserIds { get; init; } = Array.Empty<string>();
    public IEnumerable<string> NotifyUserNames { get; init; } = Enumerable.Empty<string>();
}
