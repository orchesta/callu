using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class CommunicationProviderRepository(ApplicationDbContext context, ILogger<CommunicationProviderRepository> logger)
    : Repository<CommunicationProvider>(context, logger), ICommunicationProviderRepository
{
    public async Task<IEnumerable<CommunicationProvider>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(p => p.IsEnabled && !p.IsDeleted)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<CommunicationProvider?> GetWithSipTrunkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.SipTrunk)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
    }

    public async Task<CommunicationProvider?> GetHighestPriorityEnabledNoTrackingAsync(
        CancellationToken cancellationToken = default) =>
        await _dbSet
            .AsNoTracking()
            .Where(p => p.IsEnabled && !p.IsDeleted)
            // Priority is documented "lower = higher priority", so ascending is the first choice.
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Every provider row, enabled or not — the registry needs "configured" and "active" as
    /// separate facts.</summary>
    public async Task<IReadOnlyList<CommunicationProvider>> ListForRegistryReloadAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.CommunicationProviders
            .Include(p => p.SipTrunk)
            .Where(p => !p.IsDeleted)
            // Priority defaults to 0, so ties are the normal case; the registry takes the first row.
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
    }
}
