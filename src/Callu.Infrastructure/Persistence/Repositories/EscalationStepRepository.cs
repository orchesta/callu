using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// EscalationStep repository implementation
/// </summary>
public class EscalationStepRepository(ApplicationDbContext context, ILogger<EscalationStepRepository> logger)
    : Repository<EscalationStep>(context, logger), IEscalationStepRepository
{
    public async Task<IEnumerable<EscalationStep>> GetByPolicyAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.EscalationPolicyId == policyId)
            .OrderBy(s => s.Level)
            .ToListAsync(cancellationToken);
    }

    public async Task<EscalationStep?> GetByLevelAsync(Guid policyId, int level, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(s => s.EscalationPolicyId == policyId && s.Level == level, cancellationToken);
    }
}
