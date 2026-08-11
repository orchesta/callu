using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class WebhookCaptureRepository(ApplicationDbContext context, ILogger<WebhookCaptureRepository> logger)
    : Repository<WebhookCapture>(context, logger), IWebhookCaptureRepository
{
    private const int DeleteBatchRows = 2000;

    // The service scope holds only service-token captures; a capture that arrived through an
    // integration belongs to that integration's scope even when it also carries a service id.
    private IQueryable<WebhookCapture> ServiceScope(Guid serviceId) =>
        _dbSet.Where(c => c.ServiceId == serviceId && c.IntegrationId == null);

    private IQueryable<WebhookCapture> IntegrationScope(Guid integrationId) =>
        _dbSet.Where(c => c.IntegrationId == integrationId);

    public async Task<IEnumerable<WebhookCapture>> GetByServiceAsync(
        Guid serviceId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return await ServiceScope(serviceId)
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.CapturedAt)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByServiceAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await ServiceScope(serviceId).CountAsync(c => !c.IsDeleted, cancellationToken);
    }

    public async Task<IEnumerable<WebhookCapture>> GetByIntegrationAsync(
        Guid integrationId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return await IntegrationScope(integrationId)
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.CapturedAt)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        return await IntegrationScope(integrationId).CountAsync(c => !c.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetCountsByIntegrationAsync(
        IReadOnlyCollection<Guid> integrationIds, CancellationToken cancellationToken = default)
    {
        if (integrationIds.Count == 0) return new Dictionary<Guid, int>();

        return await _dbSet
            .Where(c => c.IntegrationId != null && integrationIds.Contains(c.IntegrationId.Value) && !c.IsDeleted)
            .GroupBy(c => c.IntegrationId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    public async Task<bool> HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsRelational())
        {
            var deleted = await _dbSet.IgnoreQueryFilters()
                .Where(c => c.Id == id && !c.IsDeleted)
                .ExecuteDeleteAsync(cancellationToken);
            return deleted > 0;
        }

        var row = await _dbSet.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);
        if (row is null) return false;
        _dbSet.Remove(row);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> HardDeleteScopeAsync(
        Guid? serviceId, Guid? integrationId, CancellationToken cancellationToken = default)
    {
        var scope = integrationId is { } iid
            ? _dbSet.IgnoreQueryFilters().Where(c => c.IntegrationId == iid)
            : _dbSet.IgnoreQueryFilters().Where(c => c.ServiceId == serviceId && c.IntegrationId == null);

        // Batches keep each DELETE inside the command timeout; every batch commits on its own.
        var total = 0;
        while (true)
        {
            var ids = await scope
                .OrderBy(c => c.CapturedAt)
                .ThenBy(c => c.Id)
                .Take(DeleteBatchRows)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0) break;

            total += await HardDeleteByIdsAsync(ids, cancellationToken);
            if (ids.Count < DeleteBatchRows) break;
        }

        return total;
    }

    public async Task<int> TrimScopeForPendingInsertAsync(
        Guid? serviceId, Guid? integrationId, int cap, int maxRows, CancellationToken cancellationToken = default)
    {
        var scope = integrationId is { } iid
            ? _dbSet.IgnoreQueryFilters().Where(c => c.IntegrationId == iid)
            : _dbSet.IgnoreQueryFilters().Where(c => c.ServiceId == serviceId && c.IntegrationId == null);

        // Keep the newest cap-1 rows so the pending insert lands at the cap; bounded per call so a
        // backlog far over the cap drains across the next inserts instead of one oversized delete.
        var ids = await scope
            .OrderByDescending(c => c.CapturedAt)
            .ThenByDescending(c => c.Id)
            .Skip(cap - 1)
            .Take(maxRows)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0) return 0;

        return await HardDeleteByIdsAsync(ids, cancellationToken);
    }

    private async Task<int> HardDeleteByIdsAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            return await _dbSet.IgnoreQueryFilters()
                .Where(c => ids.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // The in-memory provider has no set-based delete; loading the rows is fine there.
        var rows = await _dbSet.IgnoreQueryFilters()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
        _dbSet.RemoveRange(rows);
        await _context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}
