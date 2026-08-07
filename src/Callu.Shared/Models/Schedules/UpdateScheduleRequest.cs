namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Update schedule request
/// </summary>
public record UpdateScheduleRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Timezone { get; init; }
    public Guid? TeamId { get; init; }
}