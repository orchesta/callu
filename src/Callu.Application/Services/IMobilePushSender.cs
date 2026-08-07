using Callu.Shared.Models.Notifications;

namespace Callu.Application.Services;

public interface IMobilePushSender
{
    Task SendToUserAsync(string userId, NotificationItemDto notification, CancellationToken cancellationToken = default);
}
