using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// Notification repository implementation
/// </summary>
public class NotificationRepository(ApplicationDbContext context, ILogger<NotificationRepository> logger)
    : Repository<Notification>(context, logger), INotificationRepository
{
    /// <summary>Idempotent add: skips the insert when the row's <see cref="Notification.DedupeKey"/> is
    /// already pending or persisted, so one duplicate cannot roll back the whole batch.</summary>
    public override async Task AddAsync(Notification entity, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(entity.DedupeKey))
        {
            var pendingDuplicate = _context.ChangeTracker.Entries<Notification>()
                .Any(e => e.State == EntityState.Added
                          && !ReferenceEquals(e.Entity, entity)
                          && e.Entity.DedupeKey == entity.DedupeKey);
            if (pendingDuplicate)
            {
                _logger.LogDebug("Skipping duplicate notification (pending) DedupeKey={DedupeKey}", entity.DedupeKey);
                return;
            }

            var alreadyPersisted = await _dbSet
                .IgnoreQueryFilters()
                .AnyAsync(n => n.DedupeKey == entity.DedupeKey, cancellationToken);
            if (alreadyPersisted)
            {
                _logger.LogDebug("Skipping duplicate notification (persisted) DedupeKey={DedupeKey}", entity.DedupeKey);
                return;
            }
        }

        await base.AddAsync(entity, cancellationToken);
    }

    public async Task<IEnumerable<Notification>> GetUnreadByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        // InboxDismissedAt is not covered by the global soft-delete query filter (that only covers
        // IsDeleted), so every inbox read has to exclude dismissed rows explicitly.
        return await _dbSet
            .Where(n => n.UserId == userId && !n.IsRead && n.InboxDismissedAt == null)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .CountAsync(n => n.UserId == userId && !n.IsRead && n.InboxDismissedAt == null
                              && (n.Type == Domain.Enums.NotificationType.Email
                                  || n.Type == Domain.Enums.NotificationType.Push),
                cancellationToken);
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default)
    {
        var notification = await _dbSet
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);
        if (notification is null)
            return false;

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        return true;
    }

    public async Task MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await _dbSet
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, now), cancellationToken);
    }
}
