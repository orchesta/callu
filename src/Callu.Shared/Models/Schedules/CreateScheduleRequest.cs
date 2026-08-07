namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Create schedule request
/// </summary>
public record CreateScheduleRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid TeamId { get; init; }
    public string Timezone { get; init; } = "UTC";
}