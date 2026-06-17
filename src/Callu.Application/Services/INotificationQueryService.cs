using Callu.Shared.Models.Notifications;

namespace Callu.Application.Services;

/// <summary>
/// Read-only notification queries for UI display (header dropdown, notifications page)
/// Separated from INotificationDispatcher which handles sending/dispatching
/// </summary>
public interface INotificationQueryService
{
    /// <summary>
    /// Get recent notifications for a user (for header dropdown)
    /// </summary>
    Task<IEnumerable<NotificationItemDto>> GetRecentAsync(string userId, int count = 10, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get unread notification count for badge display
    /// </summary>
    Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Mark a single notification as read. Scoped to the owning user (returns false if the
    /// notification does not exist or belongs to another user).
    /// </summary>
    Task<bool> MarkAsReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Mark all notifications as read for a user
    /// </summary>
    Task MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete a notification
    /// </summary>
    Task<bool> DeleteAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default);
}
