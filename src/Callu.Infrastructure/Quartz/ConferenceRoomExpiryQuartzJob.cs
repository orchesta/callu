using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>
/// Sweeps the <c>ConferenceRooms</c> table once a minute and flips any room whose
/// <c>ExpiresAt</c> has elapsed from <c>Active</c> to <c>Expired</c>. Replaces the
/// previous "lazy expiry on GET" pattern in <c>ValidateParticipantAsync</c>, which
/// only ran when a participant happened to hit the endpoint — rooms that nobody
/// joined stayed <c>Active</c> in the DB forever and accumulated.
///
/// Idempotent: rows already in a terminal status are filtered out at the WHERE clause.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ConferenceRoomExpiryQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ConferenceRoomExpiryQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var expired = await db.ConferenceRooms
            .Where(r => r.Status == ConferenceRoomStatus.Active
                        && r.ExpiresAt <= now
                        && !r.IsDeleted)
            .ToListAsync(context.CancellationToken);

        if (expired.Count == 0) return;

        foreach (var room in expired)
        {
            room.Status = ConferenceRoomStatus.Expired;
            room.UpdatedAt = now;
        }

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Expired {Count} conference rooms past ExpiresAt", expired.Count);
    }
}
