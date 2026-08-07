using System.Runtime.ExceptionServices;
using Callu.Application.Services;
using Callu.Shared.Models.Notifications;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.SignalR;

public sealed class CompositeNotificationPushService(
    SignalRNotificationPushService signalR,
    IMobilePushSender mobilePush,
    ILogger<CompositeNotificationPushService> logger) : INotificationPushService
{
    public async Task PushNotificationAsync(
        string userId, NotificationItemDto notification, CancellationToken cancellationToken = default)
    {
        // Independent legs: a backplane outage must not be the reason a phone stays quiet, and the
        // caller still learns the browser handoff failed.
        Exception? signalRFailure = null;
        try
        {
            await signalR.PushNotificationAsync(userId, notification, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            signalRFailure = ex;
        }

        try
        {
            await mobilePush.SendToUserAsync(userId, notification, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mobile push failed for user {UserId}", userId);
        }

        if (signalRFailure is not null)
            ExceptionDispatchInfo.Capture(signalRFailure).Throw();
    }

    public Task PushUnreadCountAsync(string userId, int count, CancellationToken cancellationToken = default) =>
        signalR.PushUnreadCountAsync(userId, count, cancellationToken);

    public Task BroadcastIncidentUpdateAsync(Guid incidentId, string status, CancellationToken cancellationToken = default) =>
        signalR.BroadcastIncidentUpdateAsync(incidentId, status, cancellationToken);

    public Task BroadcastServiceUpdatedAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
        signalR.BroadcastServiceUpdatedAsync(serviceId, cancellationToken);

    public Task BroadcastTeamUpdatedAsync(Guid teamId, CancellationToken cancellationToken = default) =>
        signalR.BroadcastTeamUpdatedAsync(teamId, cancellationToken);

    public Task BroadcastScheduleUpdatedAsync(Guid scheduleId, CancellationToken cancellationToken = default) =>
        signalR.BroadcastScheduleUpdatedAsync(scheduleId, cancellationToken);

    public Task BroadcastSettingsUpdatedAsync(string section, CancellationToken cancellationToken = default) =>
        signalR.BroadcastSettingsUpdatedAsync(section, cancellationToken);
}
