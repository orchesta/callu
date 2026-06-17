using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// NotificationPreference-specific repository interface
/// </summary>
public interface INotificationPreferenceRepository : IRepository<NotificationPreference>
{
    Task<NotificationPreference?> GetByUserAsync(string userId, CancellationToken cancellationToken = default);
}
