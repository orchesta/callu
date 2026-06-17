namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Create an on-call override. Times are absolute UTC instants — the operator picks a
/// specific real-world interval ("cover for me from Friday 17:00 my time") and the UI is
/// responsible for converting its local picker to UTC before hitting the API.
/// </summary>
public record CreateOverrideRequest
{
    public Guid ScheduleId { get; init; }
    public string OverrideUserId { get; init; } = string.Empty;
    public string? OriginalUserId { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public string? Reason { get; init; }
}
