using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.SignalR;

/// <summary>SignalR hub for real-time notification delivery; it lives here rather than in the API host so
/// the Worker can resolve its <c>IHubContext</c> and publish through the Redis backplane.</summary>
[Authorize]
public class NotificationHub(ILogger<NotificationHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
            logger.LogDebug("User {UserId} connected to NotificationHub", userId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
            logger.LogDebug("User {UserId} disconnected from NotificationHub", userId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
