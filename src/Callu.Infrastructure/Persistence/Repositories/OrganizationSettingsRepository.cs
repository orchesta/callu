using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class OrganizationSettingsRepository(ApplicationDbContext context, ILogger<OrganizationSettingsRepository> logger)
    : Repository<OrganizationSettings>(context, logger), IOrganizationSettingsRepository
{
    public async Task<OrganizationSettings?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync(new object[] { OrganizationSettings.SingletonId }, cancellationToken);
    }
}
