using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.BackgroundJobs;

/// <summary>Flips notifications stuck in Sending/Retrying back to Failed so the retry queue picks them up.</summary>
public sealed class NotificationReaperBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationReaperBackgroundService> logger)
    : BackgroundService
{
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private const int BatchSize = 200;
    private const int MaxConcurrencyResolveAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Notification reaper background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReclaimStuckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification reaper sweep failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Notification reaper background service stopped");
    }

    /// <summary>One sweep, factored out of the loop so tests can drive it directly.</summary>
    internal async Task ReclaimStuckAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var threshold = DateTime.UtcNow - StuckAfter;

        var stuck = await dbContext.Notifications
            .Where(n => (n.DeliveryStatus == NotificationDeliveryStatus.Sending
                         || n.DeliveryStatus == NotificationDeliveryStatus.Retrying)
                        && n.LastAttemptAt != null
                        && n.LastAttemptAt < threshold
                        && n.RetryCount < Notification.MaxRetries)
            .OrderBy(n => n.LastAttemptAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (stuck.Count == 0)
            return;

        foreach (var notification in stuck)
            notification.MarkFailed("Delivery attempt did not complete in time; reclaimed for retry");

        var conflicts = 0;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogWarning(
                    "Notification reaper reset {Count} stuck notification(s) for retry ({Conflicts} already reclaimed by another host)",
                    stuck.Count - conflicts, conflicts);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxConcurrencyResolveAttempts)
            {
                foreach (var entry in ex.Entries)
                    entry.State = EntityState.Detached;
                conflicts += ex.Entries.Count;
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "Notification reaper could not persist reset batch; a later sweep will retry");
                return;
            }
        }
    }
}
