using Callu.Shared.Models.Schedules;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for Rotation management (ISP - focused on rotations)
/// </summary>
public interface IRotationService
{
    /// <summary>
    /// Get all rotations for a schedule
    /// </summary>
    Task<IEnumerable<ScheduleRotationDto>> GetRotationsAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Add a new rotation to a schedule
    /// </summary>
    Task<ScheduleRotationDto> AddRotationAsync(Guid scheduleId, CreateRotationRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update an existing rotation. Returns the owning schedule's id on success
    /// so callers can broadcast schedule-level change notifications with the
    /// right key; returns null when the rotation is missing / soft-deleted.
    /// </summary>
    Task<Guid?> UpdateRotationAsync(Guid rotationId, UpdateRotationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete a rotation. Returns the owning schedule's id on success, null otherwise.
    /// </summary>
    Task<Guid?> RemoveRotationAsync(Guid rotationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get upcoming rotations for a schedule
    /// </summary>
    Task<IEnumerable<ScheduleRotationDto>> GetUpcomingRotationsAsync(Guid scheduleId, int days = 7, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate rotation coverage — detect gaps where no one is on-call
    /// </summary>
    Task<RotationCoverageResult> ValidateRotationCoverageAsync(Guid scheduleId, int days = 30, CancellationToken cancellationToken = default);
}

