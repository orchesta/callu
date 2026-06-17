using Callu.Shared.Models.Schedules;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for On-Call status queries (ISP - focused on on-call status only)
/// Override operations are handled by IOnCallOverrideService
/// </summary>
public interface IOnCallService
{
    /// <summary>
    /// Get current on-call status for a schedule
    /// </summary>
    Task<OnCallStatusDto?> GetCurrentOnCallAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get current on-call user for a team (across all schedules)
    /// </summary>
    Task<OnCallStatusDto?> GetCurrentOnCallForTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if a user is currently on-call
    /// </summary>
    Task<bool> IsUserOnCallAsync(string userId, CancellationToken cancellationToken = default);
}
