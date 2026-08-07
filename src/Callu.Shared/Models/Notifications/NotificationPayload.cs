namespace Callu.Shared.Models.Notifications;

/// <summary>
/// Notification payload data
/// </summary>
public record NotificationPayload
{
    public required Guid IncidentId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Severity { get; init; }
    public required NotificationEventType EventType { get; init; }
    public int EscalationLevel { get; init; }
    public string? ServiceName { get; init; }
    public string DataLanguage { get; init; } = "en-US";

    /// <summary>
    /// When true and the schedule has a secondary on-call slot, both responders are paged.
    /// </summary>
    public bool IncludeSecondaryOnCall { get; init; }

    /// <summary>Distinguishes one escalation run of an incident from the next; always derive it with <see cref="GenerationFor(DateTime?, int)"/>.</summary>
    public long DispatchGeneration { get; init; }

    /// <summary>The dedupe generation of the escalation run that began at the given time, truncated to microseconds.</summary>
    public static long GenerationFor(DateTime? escalationStartedAt)
    {
        if (escalationStartedAt is not { } startedAt) return 0;

        var ticks = startedAt.Ticks;
        return ticks - (ticks % TimeSpan.TicksPerMicrosecond);
    }

    /// <summary>The dedupe generation of one repeat pass of that run.</summary>
    // Rides in the sub-microsecond ticks the truncation leaves free; without it every pass reuses the first pass's key.
    public static long GenerationFor(DateTime? escalationStartedAt, int cyclesCompleted) =>
        GenerationFor(escalationStartedAt)
        + Math.Clamp(cyclesCompleted, 0, (int)TimeSpan.TicksPerMicrosecond - 1);
}
