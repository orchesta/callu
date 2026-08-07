using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Enums;
using Callu.Shared;
using Callu.Shared.Models.Notifications;
using Callu.Infrastructure.Persistence.Transactions;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Read-only notification queries for UI display
/// </summary>
public class NotificationQueryService(
    INotificationRepository notificationRepo,
    ITransactionManager transactionManager,
    INotificationPushService? pushService = null) : INotificationQueryService
{
    public async Task<IEnumerable<NotificationItemDto>> GetRecentAsync(
        string userId, int count = 10, CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, AppConstants.Pagination.MaxPageSize);

        var notifications = await notificationRepo.GetQueryable()
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsDeleted && n.InboxDismissedAt == null
                        && (n.Type == Domain.Enums.NotificationType.Email
                            || n.Type == Domain.Enums.NotificationType.Push))
            .OrderByDescending(n => n.CreatedAt)
            .Take(count)
            .Select(n => new NotificationItemDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Type = n.Message != null && n.Message.StartsWith("IncidentResolved") ? "resolved"
                     : n.Message != null && n.Message.StartsWith("EscalationStep") ? "escalation"
                     : n.IncidentId.HasValue ? "incident"
                     : "info",
                ActionUrl = n.ActionUrl,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        for (int i = 0; i < notifications.Count; i++)
        {
            notifications[i] = notifications[i] with { TimeAgo = FormatTimeAgo(now, notifications[i].CreatedAt) };
        }

        return notifications;
    }

    public async Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await notificationRepo.GetUnreadCountAsync(userId, cancellationToken);
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default)
    {
        var marked = await transactionManager.ExecuteInTransactionAsync(
            () => notificationRepo.MarkAsReadAsync(notificationId, userId, cancellationToken),
            cancellationToken);

        if (marked)
            await PushUnreadCountIfAvailable(userId, cancellationToken);

        return marked;
    }

    public async Task MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        await notificationRepo.MarkAllAsReadAsync(userId, cancellationToken);
        await PushUnreadCountIfAvailable(userId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default)
    {
        var wasUnread = false;
        var deleted = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var notification = await notificationRepo.GetByIdAsync(notificationId, cancellationToken);
            if (notification == null || notification.UserId != userId || notification.InboxDismissedAt != null)
                return false;

            // Bell-menu "hide from my list" — cosmetic, per-user, invisible to dispatch. This is NOT
            // a soft-delete: IsDeleted stays untouched so a Delivered page's row remains live for the
            // dedupe read-back / retry sweep and cannot be silently cancelled from the inbox.
            wasUnread = !notification.IsRead;
            notification.InboxDismissedAt = DateTime.UtcNow;
            notificationRepo.Update(notification);
            return true;
        }, cancellationToken);

        if (deleted && wasUnread)
            await PushUnreadCountIfAvailable(userId, cancellationToken);

        return deleted;
    }

    /// <summary>
    /// Recomputes the user's authoritative unread count post-commit and pushes it
    /// via SignalR. Null pushService → silent no-op (e.g. Worker without backplane).
    /// </summary>
    private async Task PushUnreadCountIfAvailable(string userId, CancellationToken cancellationToken)
    {
        if (pushService is null) return;
        try
        {
            var count = await notificationRepo.GetUnreadCountAsync(userId, cancellationToken);
            await pushService.PushUnreadCountAsync(userId, count, cancellationToken);
        }
        catch
        {
        }
    }

    private static string FormatTimeAgo(DateTime now, DateTime createdAt)
    {
        var span = now - createdAt;

        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} hour{((int)span.TotalHours != 1 ? "s" : "")} ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} day{((int)span.TotalDays != 1 ? "s" : "")} ago";
        if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)} week{((int)(span.TotalDays / 7) != 1 ? "s" : "")} ago";
        return createdAt.ToString("MMM d, yyyy");
    }
}
