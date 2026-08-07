using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// Team repository implementation
/// </summary>
public class TeamRepository(ApplicationDbContext context, ILogger<TeamRepository> logger)
    : Repository<Team>(context, logger), ITeamRepository
{
    public async Task<Team?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(t => EF.Functions.ILike(t.Name, name), cancellationToken);
    }

    public async Task<Team?> GetWithMembersAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Team>> GetTeamsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => t.Members.Any(m => m.UserId == userId))
            .ToListAsync(cancellationToken);
    }
}
