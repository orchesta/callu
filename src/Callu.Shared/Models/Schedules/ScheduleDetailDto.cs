namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Schedule detail with rotations
/// </summary>
public record ScheduleDetailDto : ScheduleDto
{
    public List<ScheduleRotationDto> Rotations { get; init; } = new();
    public List<OnCallOverrideDto> Overrides { get; init; } = new();
}