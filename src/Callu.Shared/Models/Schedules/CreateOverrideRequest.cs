namespace Callu.Shared.Models.Schedules;

/// <summary>Create an on-call override; times are absolute UTC instants, converted from local by the caller.</summary>
public record CreateOverrideRequest
{
    public Guid ScheduleId { get; init; }
    public string OverrideUserId { get; init; } = string.Empty;
    public string? OriginalUserId { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public string? Reason { get; init; }
}
