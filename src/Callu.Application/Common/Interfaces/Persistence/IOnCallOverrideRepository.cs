using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// OnCallOverride-specific repository interface
/// </summary>
public interface IOnCallOverrideRepository : IRepository<OnCallOverride>
{
    Task<IEnumerable<OnCallOverride>> GetByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<OnCallOverride?> GetActiveOverrideAsync(Guid scheduleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<OnCallOverride>> GetByUserAsync(string userId, CancellationToken cancellationToken = default);
}
