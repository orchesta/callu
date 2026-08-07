using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// EscalationPolicy repository implementation
/// </summary>
public class EscalationPolicyRepository(ApplicationDbContext context, ILogger<EscalationPolicyRepository> logger)
    : Repository<EscalationPolicy>(context, logger), IEscalationPolicyRepository
{
    public async Task<EscalationPolicy?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => EF.Functions.ILike(p.Name, name), cancellationToken);
    }

    public async Task<EscalationPolicy?> GetWithStepsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Steps.OrderBy(s => s.Level))
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<EscalationPolicy>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(p => p.TeamId == teamId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<EscalationPolicy>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<EscalationPolicy?> GetActiveForTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        // Oldest active wins, deliberately: a second active policy is a misconfiguration, and a
        // predictable ladder beats guessing that the newest one was meant to take over paging.
        var active = await _dbSet
            .Where(p => p.TeamId == teamId && p.IsActive && !p.IsDeleted)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (active.Count > 1)
            logger.LogWarning(
                "Team {TeamId} has more than one active escalation policy; paging {PolicyId} ({PolicyName}). "
                + "Deactivate the others — which one pages is otherwise ambiguous.",
                teamId, active[0].Id, active[0].Name);

        return active.FirstOrDefault();
    }
}
