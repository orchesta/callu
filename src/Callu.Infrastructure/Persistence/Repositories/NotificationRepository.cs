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
    /// <summary>
    /// Idempotent add. Notifications carry a deterministic <see cref="Notification.DedupeKey"/>
    /// backed by the partial unique index <c>IX_Notifications_DedupeKey</c>. A duplicate key —
    /// a replayed escalation message, an operator re-page, or the same user appearing twice in a
    /// step's target list — would otherwise surface as a 23505 at the batch <c>SaveChanges</c> and
    /// roll back EVERY notification queued in that unit of work, turning the dedupe index into a
    /// denial-of-delivery for the whole page. We therefore skip the insert when the key is already
    /// pending in this DbContext or already persisted, so one duplicate can't sink the batch.
    /// Rows without a DedupeKey are added unconditionally (no idempotency claim).
    /// </summary>
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
        return await _dbSet
            .Where(n => n.UserId == userId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .CountAsync(n => n.UserId == userId && !n.IsRead
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

    public async Task<IEnumerable<Notification>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(n => !n.IsSent && n.RetryCount < 3)
            .OrderBy(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
