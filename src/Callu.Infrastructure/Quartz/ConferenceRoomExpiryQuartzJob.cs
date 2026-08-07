using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Flips conference rooms whose <c>ExpiresAt</c> has elapsed from <c>Active</c> to
/// <c>Expired</c>; idempotent, since terminal rows are filtered out in the WHERE clause.</summary>
[DisallowConcurrentExecution]
public sealed class ConferenceRoomExpiryQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ConferenceRoomExpiryQuartzJob> logger)
    : IJob
{
    // Bounded batch + per-entry conflict resolve, as the notification reaper does.
    private const int BatchSize = 200;
    private const int MaxConcurrencyResolveAttempts = 5;

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var expired = await db.ConferenceRooms
            .Where(r => r.Status == ConferenceRoomStatus.Active
                        && r.ExpiresAt <= now
                        && !r.IsDeleted)
            .OrderBy(r => r.ExpiresAt)
            .ThenBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(context.CancellationToken);

        if (expired.Count == 0) return;

        foreach (var room in expired)
        {
            room.Status = ConferenceRoomStatus.Expired;
            room.UpdatedAt = now;
        }

        var conflicts = 0;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(context.CancellationToken);
                logger.LogInformation(
                    "Expired {Count} conference rooms past ExpiresAt ({Conflicts} left to the next sweep)",
                    expired.Count - conflicts, conflicts);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxConcurrencyResolveAttempts)
            {
                foreach (var entry in ex.Entries)
                    entry.State = EntityState.Detached;
                conflicts += ex.Entries.Count;
            }
        }
    }
}
