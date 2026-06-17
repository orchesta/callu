using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Callu.Application.Services;
using Callu.Shared.Models.Notifications;

namespace Callu.Infrastructure.SignalR;

/// <summary>
/// SignalR implementation of INotificationPushService. Lives in Infrastructure so
/// both API and Worker hosts can resolve it — both share the Redis backplane, so
/// a Worker-published event reaches every API-connected client.
/// </summary>
public class SignalRNotificationPushService(
    IHubContext<NotificationHub> hubContext,
    ILogger<SignalRNotificationPushService> logger)
    : INotificationPushService
{
    public async Task PushNotificationAsync(string userId, NotificationItemDto notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.Group($"user-{userId}")
                .SendAsync("ReceiveNotification", notification, cancellationToken);

            logger.LogDebug("Pushed notification to user {UserId}: {Title}", userId, notification.Title);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push notification to user {UserId}", userId);
        }
    }

    public async Task PushUnreadCountAsync(string userId, int count, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.Group($"user-{userId}")
                .SendAsync("UpdateUnreadCount", count, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push unread count to user {UserId}", userId);
        }
    }

    public async Task BroadcastIncidentUpdateAsync(Guid incidentId, string status, CancellationToken cancellationToken = default)
    {
        try
        {
            var clients = hubContext.Clients.All;
            await clients.SendAsync("IncidentUpdated", incidentId, status, cancellationToken);
            await clients.SendAsync("BroadcastIncidentUpdate", incidentId.ToString(), status, cancellationToken);

            logger.LogDebug("Broadcast incident update: {IncidentId} -> {Status}", incidentId, status);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast incident update {IncidentId}", incidentId);
        }
    }

    public async Task BroadcastServiceUpdatedAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync("ServiceUpdated", serviceId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast service update {ServiceId}", serviceId);
        }
    }

    public async Task BroadcastTeamUpdatedAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync("TeamUpdated", teamId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast team update {TeamId}", teamId);
        }
    }

    public async Task BroadcastScheduleUpdatedAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync("ScheduleUpdated", scheduleId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast schedule update {ScheduleId}", scheduleId);
        }
    }

    public async Task BroadcastSettingsUpdatedAsync(string section, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync("SettingsUpdated", section, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast settings update for section {Section}", section);
        }
    }
}
