using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class CallLogRepository(ApplicationDbContext context, ILogger<CallLogRepository> logger)
    : Repository<CallLog>(context, logger), ICallLogRepository
{
    public async Task<IEnumerable<CallLog>> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(cl => cl.IncidentId == incidentId)
            .OrderByDescending(cl => cl.InitiatedAt)
            .ToListAsync(cancellationToken);
    }
}
