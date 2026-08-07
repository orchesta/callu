using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Team-specific repository interface
/// </summary>
public interface ITeamRepository : IRepository<Team>
{
    Task<Team?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Team?> GetWithMembersAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Team>> GetTeamsForUserAsync(string userId, CancellationToken cancellationToken = default);
}
