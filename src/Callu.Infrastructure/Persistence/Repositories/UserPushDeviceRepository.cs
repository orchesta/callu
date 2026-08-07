using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class UserPushDeviceRepository(ApplicationDbContext context, ILogger<UserPushDeviceRepository> logger)
    : Repository<UserPushDevice>(context, logger), IUserPushDeviceRepository
{
    public Task<UserPushDevice?> GetByTokenAsync(string pushToken, CancellationToken cancellationToken = default) =>
        _dbSet.FirstOrDefaultAsync(d => d.PushToken == pushToken, cancellationToken);

    public async Task<IReadOnlyList<UserPushDevice>> GetActiveByUserIdAsync(
        string userId, CancellationToken cancellationToken = default) =>
        await _dbSet
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt)
            .ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);
}
