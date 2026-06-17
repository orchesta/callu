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
        return await _dbSet
            .FirstOrDefaultAsync(np => np.UserId == userId, cancellationToken);
    }
}
