using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;
using NodaTime;

namespace Callu.Infrastructure.Persistence.Repositories;

public class OnCallOverrideRepository(ApplicationDbContext context, ILogger<OnCallOverrideRepository> logger)
    : Repository<OnCallOverride>(context, logger), IOnCallOverrideRepository
{
    public async Task<IEnumerable<OnCallOverride>> GetByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(o => o.ScheduleId == scheduleId)
            .OrderByDescending(o => o.StartUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<OnCallOverride?> GetActiveOverrideAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return await _dbSet
            .Where(o => o.ScheduleId == scheduleId && o.IsActive && o.StartUtc <= now && o.EndUtc > now)
            .OrderByDescending(o => o.StartUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<OnCallOverride>> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(o => o.OverrideUserId == userId)
            .Include(o => o.Schedule)
            .OrderByDescending(o => o.StartUtc)
            .ToListAsync(cancellationToken);
    }
}
