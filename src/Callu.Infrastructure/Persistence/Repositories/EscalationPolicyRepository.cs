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
}
