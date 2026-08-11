using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class WebhookCaptureRepository(ApplicationDbContext context, ILogger<WebhookCaptureRepository> logger)
    : Repository<WebhookCapture>(context, logger), IWebhookCaptureRepository
{
    public async Task<IEnumerable<WebhookCapture>> GetByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.ServiceId == serviceId && !c.IsDeleted)
            .OrderByDescending(c => c.CapturedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.CountAsync(c => c.ServiceId == serviceId && !c.IsDeleted, cancellationToken);
    }

    public async Task<IEnumerable<WebhookCapture>> GetByIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.IntegrationId == integrationId && !c.IsDeleted)
            .OrderByDescending(c => c.CapturedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        return await _dbSet.CountAsync(c => c.IntegrationId == integrationId && !c.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetCountsByIntegrationAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.IntegrationId != null && !c.IsDeleted)
            .GroupBy(c => c.IntegrationId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    public async Task<int> TrimScopeForPendingInsertAsync(
        Guid? serviceId, Guid? integrationId, int cap, int maxRows, CancellationToken cancellationToken = default)
    {
        // A capture that arrived through an integration belongs to that integration's scope even when
        // it also carries a service id; the service scope holds only service-token captures.
        var scope = integrationId is { } iid
            ? _dbSet.IgnoreQueryFilters().Where(c => c.IntegrationId == iid)
            : _dbSet.IgnoreQueryFilters().Where(c => c.ServiceId == serviceId && c.IntegrationId == null);

        var total = await scope.CountAsync(cancellationToken);
        var excess = total + 1 - cap;
        if (excess <= 0) return 0;

        // Bounded per call: a backlog far over the cap drains across the next inserts instead of
        // one oversized delete that could outlive the request.
        var ids = await scope
            .OrderBy(c => c.CapturedAt)
            .ThenBy(c => c.Id)
            .Take(Math.Min(excess, maxRows))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0) return 0;

        if (_context.Database.IsRelational())
        {
            await _dbSet.IgnoreQueryFilters()
                .Where(c => ids.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            // The in-memory provider has no set-based delete; loading the rows is fine there.
            var rows = await _dbSet.IgnoreQueryFilters()
                .Where(c => ids.Contains(c.Id))
                .ToListAsync(cancellationToken);
            _dbSet.RemoveRange(rows);
        }

        return ids.Count;
    }
}
