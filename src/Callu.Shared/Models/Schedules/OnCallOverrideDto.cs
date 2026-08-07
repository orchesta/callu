namespace Callu.Shared.Models.Schedules;

public record OnCallOverrideDto
{
    public Guid Id { get; init; }
    public Guid ScheduleId { get; init; }
    public string ScheduleName { get; init; } = string.Empty;
    public string OverrideUserId { get; init; } = string.Empty;
    public string? OverrideUserName { get; init; }
    public string? OverrideUserInitials { get; init; }
    public string? OriginalUserId { get; init; }
    public string? OriginalUserName { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public string? Reason { get; init; }
    public bool IsActive { get; init; }
    public bool IsCurrent => IsActive && DateTime.UtcNow >= StartUtc && DateTime.UtcNow < EndUtc;
}
