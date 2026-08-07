using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class SipTrunkSettingsRepository(ApplicationDbContext context, ILogger<SipTrunkSettingsRepository> logger)
    : Repository<SipTrunkSettings>(context, logger), ISipTrunkSettingsRepository
{
    public async Task<IEnumerable<SipTrunkSettings>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => t.IsEnabled && !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<SipTrunkSettings?> GetByIdIgnoringFiltersNoTrackingAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        await _context.SipTrunkSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
}
