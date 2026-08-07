namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Current on-call status
/// </summary>
public record OnCallStatusDto
{
    public Guid ScheduleId { get; init; }
    public string ScheduleName { get; init; } = string.Empty;
    public string? PrimaryUserId { get; init; }
    public string? PrimaryUserName { get; init; }
    public string? PrimaryUserInitials { get; init; }
    public string? SecondaryUserId { get; init; }
    public string? SecondaryUserName { get; init; }
    public string? SecondaryUserInitials { get; init; }
    public DateTime? NextRotation { get; init; }
    public string? NextOnCallUserName { get; init; }
}