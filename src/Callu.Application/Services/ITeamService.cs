using Callu.Shared.Models.Teams;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for Team management
/// </summary>
public interface ITeamService
{
    /// <summary>
    /// Get all teams
    /// </summary>
    Task<IEnumerable<TeamDto>> GetTeamsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get team by ID with members
    /// </summary>
    Task<TeamDetailDto?> GetTeamByIdAsync(Guid teamId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create a new team
    /// </summary>
    Task<TeamDto> CreateTeamAsync(CreateTeamRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update an existing team. Throws <c>NotFoundException</c> when the team does not exist.
    /// </summary>
    Task UpdateTeamAsync(Guid teamId, UpdateTeamRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a team (soft delete). Throws <c>NotFoundException</c> when the team does not exist.
    /// </summary>
    Task DeleteTeamAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>Add a member to a team. Throws <c>ValidationException</c> for an unknown role,
    /// <c>NotFoundException</c> for a missing team, <c>ConflictException</c> for an existing active member.</summary>
    Task AddMemberAsync(Guid teamId, string userId, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a member from a team. Throws <c>NotFoundException</c> when the membership does not exist.
    /// </summary>
    Task RemoveMemberAsync(Guid teamId, Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a member's role in a team. Throws <c>ValidationException</c> for an unknown role
    /// and <c>NotFoundException</c> when the membership does not exist.
    /// </summary>
    Task UpdateMemberRoleAsync(Guid teamId, Guid memberId, string newRole, CancellationToken cancellationToken = default);
}

