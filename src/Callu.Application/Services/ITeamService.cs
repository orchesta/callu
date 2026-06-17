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
    /// Update an existing team
    /// </summary>
    Task<bool> UpdateTeamAsync(Guid teamId, UpdateTeamRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete a team (soft delete)
    /// </summary>
    Task<bool> DeleteTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Add a member to a team
    /// </summary>
    Task<bool> AddMemberAsync(Guid teamId, string userId, string role, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Remove a member from a team
    /// </summary>
    Task<bool> RemoveMemberAsync(Guid teamId, Guid memberId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update a member's role in a team
    /// </summary>
    Task<bool> UpdateMemberRoleAsync(Guid teamId, Guid memberId, string newRole, CancellationToken cancellationToken = default);
}

