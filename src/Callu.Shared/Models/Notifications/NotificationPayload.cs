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
    /// When true and the schedule has a secondary on-call slot, both primary
    /// and secondary responders are paged. Default off preserves the prior
    /// "primary only" behaviour for callers that haven't opted in. Set on
    /// escalation steps that explicitly want dual paging. Fix 05.F11.
    /// </summary>
    public bool IncludeSecondaryOnCall { get; init; }
}
