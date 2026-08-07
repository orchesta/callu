using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class FirebaseSettingsRepository(ApplicationDbContext context, ILogger<FirebaseSettingsRepository> logger)
    : Repository<FirebaseSettings>(context, logger), IFirebaseSettingsRepository
{
    public Task<FirebaseSettings?> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        _dbSet
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
