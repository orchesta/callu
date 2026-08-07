using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Schedule-specific repository interface
/// </summary>
public interface IScheduleRepository : IRepository<Schedule>
{
    Task<Schedule?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Schedule?> GetWithRotationsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Schedule>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<string?> GetCurrentOnCallUserAsync(Guid scheduleId, CancellationToken cancellationToken = default);
}
