using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// EscalationPolicy-specific repository interface
/// </summary>
public interface IEscalationPolicyRepository : IRepository<EscalationPolicy>
{
    Task<EscalationPolicy?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<EscalationPolicy?> GetWithStepsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<EscalationPolicy>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IEnumerable<EscalationPolicy>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The single active policy that pages this team; warns when more than one is active.
    /// </summary>
    Task<EscalationPolicy?> GetActiveForTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
}
