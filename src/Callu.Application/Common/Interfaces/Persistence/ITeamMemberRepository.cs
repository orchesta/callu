using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// TeamMember-specific repository interface
/// </summary>
public interface ITeamMemberRepository : IRepository<TeamMember>
{
    Task<IEnumerable<TeamMember>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IEnumerable<TeamMember>> GetByUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<TeamMember?> GetByTeamAndUserAsync(Guid teamId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up a membership row even if it has been soft-deleted. Used by
    /// AddMember to detect a previously-removed member and undelete the row
    /// rather than colliding on the partial unique index.
    /// </summary>
    Task<TeamMember?> GetByTeamAndUserIncludingDeletedAsync(Guid teamId, string userId, CancellationToken cancellationToken = default);
}
