using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// Schedule repository implementation
/// </summary>
public class ScheduleRepository(ApplicationDbContext context, ILogger<ScheduleRepository> logger)
    : Repository<Schedule>(context, logger), IScheduleRepository
{
    public async Task<Schedule?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(s => EF.Functions.ILike(s.Name, name), cancellationToken);
    }

    public async Task<Schedule?> GetWithRotationsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(s => s.Rotations.OrderBy(r => r.Order).ThenBy(r => r.HandoverStartLocal))
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Schedule>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.TeamId == teamId)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> GetCurrentOnCallUserAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var now = NodaTime.SystemClock.Instance.GetCurrentInstant();

        var activeOverride = await _context.OnCallOverrides
            .Where(o => o.ScheduleId == scheduleId && o.IsActive && o.StartUtc <= now && o.EndUtc > now)
            .OrderByDescending(o => o.StartUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeOverride != null)
        {
            return activeOverride.OverrideUserId;
        }

        var occurrence = await _context.ScheduleOccurrences
            .Where(o => o.ScheduleId == scheduleId &&
                        !o.IsDeleted &&
                        o.IsPrimary &&
                        o.StartUtc <= now &&
                        o.EndUtc > now)
            .OrderBy(o => o.Order)
            .FirstOrDefaultAsync(cancellationToken);

        return occurrence?.UserId;
    }
}
