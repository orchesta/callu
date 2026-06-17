using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;
using NodaTime;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// Rotation repository. NOTE: "current" and "upcoming" queries on rotations themselves are
/// no longer meaningful — rotations are templates. For "who's on-call now?" use
/// <c>IOnCallService</c> which reads materialized <c>ScheduleOccurrence</c> rows.
/// </summary>
public class ScheduleRotationRepository(ApplicationDbContext context, ILogger<ScheduleRotationRepository> logger)
    : Repository<ScheduleRotation>(context, logger), IScheduleRotationRepository
{
    public async Task<IEnumerable<ScheduleRotation>> GetByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(r => r.ScheduleId == scheduleId)
            .OrderBy(r => r.Order)
            .ThenBy(r => r.HandoverStartLocal)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ScheduleRotation>> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(r => r.UserId == userId)
            .Include(r => r.Schedule)
            .OrderBy(r => r.HandoverStartLocal)
            .ToListAsync(cancellationToken);
    }

    public async Task<ScheduleRotation?> GetCurrentRotationAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var occurrence = await _context.ScheduleOccurrences
            .Where(o => o.ScheduleId == scheduleId &&
                        !o.IsDeleted &&
                        o.IsPrimary &&
                        o.StartUtc <= now &&
                        o.EndUtc > now)
            .OrderBy(o => o.Order)
            .FirstOrDefaultAsync(cancellationToken);

        if (occurrence is null) return null;
        return await _dbSet
            .FirstOrDefaultAsync(r => r.Id == occurrence.RotationId, cancellationToken);
    }

    public async Task<IEnumerable<ScheduleRotation>> GetUpcomingAsync(Guid scheduleId, int days = 7, CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var horizon = now + Duration.FromDays(days);

        var rotationIds = await _context.ScheduleOccurrences
            .Where(o => o.ScheduleId == scheduleId &&
                        !o.IsDeleted &&
                        o.StartUtc >= now &&
                        o.StartUtc <= horizon)
            .Select(o => o.RotationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return await _dbSet
            .Where(r => rotationIds.Contains(r.Id))
            .OrderBy(r => r.Order)
            .ThenBy(r => r.HandoverStartLocal)
            .ToListAsync(cancellationToken);
    }
}
