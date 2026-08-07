using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class NotificationPreferenceRepository(ApplicationDbContext context, ILogger<NotificationPreferenceRepository> logger)
    : Repository<NotificationPreference>(context, logger), INotificationPreferenceRepository
{
    public async Task<NotificationPreference?> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        // Ordered: nothing enforces one row per user, so an unordered read picks at random.
        return await _dbSet
            .Where(np => np.UserId == userId)
            .OrderBy(np => np.CreatedAt)
            .ThenBy(np => np.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
