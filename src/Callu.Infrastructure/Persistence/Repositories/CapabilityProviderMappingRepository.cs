using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class CapabilityProviderMappingRepository(ApplicationDbContext context, ILogger<CapabilityProviderMappingRepository> logger)
    : Repository<CapabilityProviderMapping>(context, logger), ICapabilityProviderMappingRepository
{
    public async Task<CapabilityProviderMapping?> GetByCapabilityAsync(CommunicationCapability capability, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(m => m.Capability == capability && !m.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<CapabilityProviderMapping>> ListEnabledForRegistryReloadAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.CapabilityProviderMappings
            .Where(m => m.IsEnabled && !m.IsDeleted)
            .OrderBy(m => m.Priority)
            .ToListAsync(cancellationToken);
    }
}
