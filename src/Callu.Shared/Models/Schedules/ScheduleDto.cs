namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Schedule list item DTO
/// </summary>
public record ScheduleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid TeamId { get; init; }
    public string? TeamName { get; init; }
    public string Timezone { get; init; } = "UTC";
    public string? CurrentOnCallUser { get; init; }
    public int RotationCount { get; init; }
    public DateTime CreatedAt { get; init; }
}