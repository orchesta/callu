using Callu.Shared.Models.Schedules;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for On-Call Override management
/// </summary>
public interface IOnCallOverrideService
{
    /// <summary>
    /// Get all overrides for a schedule
    /// </summary>
    Task<IEnumerable<OnCallOverrideDto>> GetOverridesAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active/upcoming overrides for a schedule
    /// </summary>
    Task<IEnumerable<OnCallOverrideDto>> GetActiveOverridesAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get override by ID
    /// </summary>
    Task<OnCallOverrideDto?> GetOverrideByIdAsync(Guid overrideId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new override
    /// </summary>
    Task<OnCallOverrideDto> CreateOverrideAsync(CreateOverrideRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing override. Returns the owning schedule's id on success
    /// so the controller can broadcast a schedule-update event with the right
    /// key; null when the override is missing / soft-deleted.
    /// </summary>
    Task<Guid?> UpdateOverrideAsync(Guid overrideId, UpdateOverrideRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete an override. Returns the owning schedule's id on success, null otherwise.
    /// </summary>
    Task<Guid?> DeleteOverrideAsync(Guid overrideId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current on-call user for a schedule, considering overrides
    /// Returns the override user if an active override exists, otherwise null
    /// </summary>
    Task<string?> GetOverrideUserIdAsync(Guid scheduleId, DateTime atTime, CancellationToken cancellationToken = default);
}
