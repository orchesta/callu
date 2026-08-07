using Callu.Shared.Models.Schedules;

namespace Callu.Application.Services;

/// <summary>Schedule CRUD; rotations live on <see cref="IRotationService"/> and on-call status on
/// <c>IOnCallService</c>.</summary>
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

    /// <summary>Apply a schedule's settings and its whole rotation list in one transaction, then rematerialize
    /// once, so a multi-rotation change cannot go live half-applied. False when the schedule does not exist.</summary>
    Task<bool> SaveSchedulePlanAsync(Guid scheduleId, SaveSchedulePlanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a schedule
    /// </summary>
    Task<bool> DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default);
}


