using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.SignalR;

/// <summary>
/// SignalR hub for real-time notification delivery. Hub lives in Callu.Infrastructure
/// (not Callu.Api) so the Worker host can take a project reference and resolve
/// <c>IHubContext&lt;NotificationHub&gt;</c> to publish through the Redis backplane —
/// even though only the API process maps the hub endpoint.
///
/// Clients join user-specific groups for targeted pushes; broadcasts go to Clients.All.
/// </summary>
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
