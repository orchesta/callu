using Callu.Shared.Models.Notifications;

namespace Callu.Application.Services;

/// <summary>
/// Dispatches notifications to users via various channels
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Send a notification to specific users. Returns the number of recipients actually
    /// reached — i.e. for whom at least one channel was attempted (a durable Notification
    /// row was queued). A return of 0 means nobody was paged (no usable contacts / all
    /// channels disabled), which callers should surface rather than treat as success.
    /// </summary>
    Task<int> NotifyUsersAsync(IEnumerable<string> userIds, NotificationPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a notification to on-call user(s) from a schedule. Returns the number of
    /// recipients reached; 0 when the schedule has no current on-call responder.
    /// </summary>
    Task<int> NotifyOnCallAsync(Guid scheduleId, NotificationPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a notification to the members of a team. When <paramref name="notifyAllMembers"/>
    /// is true every team member receives it; otherwise only the on-call member of the team's
    /// primary schedule is paged. Returns the number of recipients reached; 0 when the team
    /// has no members (all-members mode) or nobody is currently on-call (on-call mode).
    /// </summary>
    Task<int> NotifyTeamAsync(Guid teamId, NotificationPayload payload, bool notifyAllMembers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process notification queue (called by background service)
    /// </summary>
    Task ProcessNotificationQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a test notification to verify channel configuration
    /// </summary>
    Task<(bool Success, string Message)> SendTestNotificationAsync(string userId, string channel, CancellationToken cancellationToken = default);
}
