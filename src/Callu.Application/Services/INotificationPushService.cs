using Callu.Shared.Models.Notifications;

namespace Callu.Application.Services;

/// <summary>Real-time notification push to connected clients. <b>Not a paging channel and never counted as
/// one</b> — a push to a hub group with no connected client succeeds and wakes nobody.</summary>
public interface INotificationPushService
{
    /// <summary>Push one notification to a user. <b>Throws</b> if it could not be handed to the hub; a successful
    /// return means the hub took it, not that anybody was listening.</summary>
    Task PushNotificationAsync(string userId, NotificationItemDto notification, CancellationToken cancellationToken = default);

    Task PushUnreadCountAsync(string userId, int count, CancellationToken cancellationToken = default);

    Task BroadcastIncidentUpdateAsync(Guid incidentId, string status, CancellationToken cancellationToken = default);

    Task BroadcastServiceUpdatedAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task BroadcastTeamUpdatedAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task BroadcastScheduleUpdatedAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task BroadcastSettingsUpdatedAsync(string section, CancellationToken cancellationToken = default);
}
