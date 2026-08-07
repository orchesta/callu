using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// ScheduleRotation-specific repository interface
/// </summary>
public interface IScheduleRotationRepository : IRepository<ScheduleRotation>
{
    Task<IEnumerable<ScheduleRotation>> GetByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ScheduleRotation>> GetByUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<ScheduleRotation?> GetCurrentRotationAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ScheduleRotation>> GetUpcomingAsync(Guid scheduleId, int days = 7, CancellationToken cancellationToken = default);
}
