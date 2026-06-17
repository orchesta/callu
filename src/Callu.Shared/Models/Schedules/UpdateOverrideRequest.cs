namespace Callu.Shared.Models.Schedules;

public record UpdateOverrideRequest
{
    public string? OverrideUserId { get; init; }
    public DateTime? StartUtc { get; init; }
    public DateTime? EndUtc { get; init; }
    public string? Reason { get; init; }
}
