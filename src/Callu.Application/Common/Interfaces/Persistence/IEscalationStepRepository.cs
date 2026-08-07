using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// EscalationStep-specific repository interface
/// </summary>
public interface IEscalationStepRepository : IRepository<EscalationStep>
{
    Task<IEnumerable<EscalationStep>> GetByPolicyAsync(Guid policyId, CancellationToken cancellationToken = default);
    Task<EscalationStep?> GetByLevelAsync(Guid policyId, int level, CancellationToken cancellationToken = default);
}
