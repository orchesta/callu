using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// TeamMember repository implementation
/// </summary>
public class TeamMemberRepository(ApplicationDbContext context, ILogger<TeamMemberRepository> logger)
    : Repository<TeamMember>(context, logger), ITeamMemberRepository
{
    public async Task<IEnumerable<TeamMember>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(m => m.TeamId == teamId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<TeamMember>> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(m => m.UserId == userId)
            .Include(m => m.Team)
            .ToListAsync(cancellationToken);
    }

    public async Task<TeamMember?> GetByTeamAndUserAsync(Guid teamId, string userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, cancellationToken);
    }

    public async Task<TeamMember?> GetByTeamAndUserIncludingDeletedAsync(Guid teamId, string userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, cancellationToken);
    }
}
