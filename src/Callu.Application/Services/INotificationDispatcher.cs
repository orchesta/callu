using Callu.Shared.Models.Notifications;

namespace Callu.Application.Services;

/// <summary>
/// Dispatches notifications to users via various channels
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Send a notification to specific users. See <see cref="NotificationDispatchResult"/>: a zero
    /// <c>Reached</c> means "nobody was there to page" ONLY when <c>Failed</c> is zero as well.
    /// </summary>
    Task<NotificationDispatchResult> NotifyUsersAsync(IEnumerable<string> userIds, NotificationPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a notification to on-call user(s) from a schedule. A schedule with no current on-call
    /// responder yields <see cref="NotificationDispatchResult.Nobody"/>.
    /// </summary>
    Task<NotificationDispatchResult> NotifyOnCallAsync(Guid scheduleId, NotificationPayload payload, CancellationToken cancellationToken = default);

    /// <summary>Send a notification to a team: every member when <paramref name="notifyAllMembers"/> is true,
    /// otherwise only the on-call member of the team's primary schedule.</summary>
    Task<NotificationDispatchResult> NotifyTeamAsync(Guid teamId, NotificationPayload payload, bool notifyAllMembers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process notification queue (called by background service)
    /// </summary>
    Task ProcessNotificationQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a test notification to verify channel configuration
    /// </summary>
    Task<(bool Success, string Message)> SendTestNotificationAsync(string userId, string channel, CancellationToken cancellationToken = default);
}
