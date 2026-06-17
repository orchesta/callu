using Callu.Shared.Models.Schedules;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for Schedule CRUD operations (ISP - focused on schedules)
/// For rotation management, use IRotationService
/// For on-call status, use IOnCallService
/// </summary>
public interface IScheduleService
{
    /// <summary>
    /// Get all schedules
    /// </summary>
    Task<IEnumerable<ScheduleDto>> GetSchedulesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get schedule by ID with details
    /// </summary>
    Task<ScheduleDetailDto?> GetScheduleByIdAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get schedules for a specific team
    /// </summary>
    Task<IEnumerable<ScheduleDto>> GetSchedulesByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create a new schedule
    /// </summary>
    Task<ScheduleDto> CreateScheduleAsync(CreateScheduleRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update an existing schedule
    /// </summary>
    Task<bool> UpdateScheduleAsync(Guid scheduleId, UpdateScheduleRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete a schedule
    /// </summary>
    Task<bool> DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default);
}


