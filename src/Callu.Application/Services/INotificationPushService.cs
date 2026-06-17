using Callu.Shared.Models.Notifications;

namespace Callu.Application.Services;

/// <summary>
/// Abstraction for real-time notification push to connected clients.
/// Implemented via SignalR in the Web layer.
/// </summary>
public interface INotificationPushService
{
    Task PushNotificationAsync(string userId, NotificationItemDto notification, CancellationToken cancellationToken = default);

    Task PushUnreadCountAsync(string userId, int count, CancellationToken cancellationToken = default);

    Task BroadcastIncidentUpdateAsync(Guid incidentId, string status, CancellationToken cancellationToken = default);

    Task BroadcastServiceUpdatedAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task BroadcastTeamUpdatedAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task BroadcastScheduleUpdatedAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task BroadcastSettingsUpdatedAsync(string section, CancellationToken cancellationToken = default);
}
