using System.Linq.Expressions;
using Callu.Domain.Base;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Daily prune of unbounded operational tables, in bounded batches and only for the tables the
/// operator gave a positive <c>Callu:Retention:*Days</c> window.</summary>
[DisallowConcurrentExecution]
public sealed class RetentionPruneQuartzJob(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    ILogger<RetentionPruneQuartzJob> logger)
    : IJob
{
    internal const int DefaultBatchSize = 5_000;
    internal const int DefaultMaxRowsPerTable = 500_000;
    private const int BatchCommandTimeoutSeconds = 120;

    internal sealed record RetentionTarget(string Table, int Days, Func<CancellationToken, Task<int>> PruneAsync);

    public async Task Execute(IJobExecutionContext context)
    {
        var retention = options.Value;
        if (!retention.AnyEnabled)
            return;

        var ct = context.CancellationToken;
        var now = DateTime.UtcNow;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.SetCommandTimeout(BatchCommandTimeoutSeconds);

        // Archiving on means the archive outlives the database, so a row that has not reached it yet
        // must not be deleted. Enabled with nothing archived caps the cutoff at nothing.
        var archive = scope.ServiceProvider.GetRequiredService<Audit.AuditArchiveService>();
        var archivedThrough = await archive.ArchivedThroughAsync(ct);
        DateTime? auditCutoffCap = options.Value.AuditLogDays > 0 && archive.Enabled
            ? (archivedThrough?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? DateTime.MinValue)
            : null;

        RetentionTarget[] targets =
        [
            Target(db.Notifications, retention.NotificationDays, "Notifications", now),
            // The hash chain reads what is left as a prefix, so what goes has to be one too:
            // a hole in the middle is indistinguishable from a deleted entry.
            Target(db.AuditLogs, retention.AuditLogDays, "AuditLogs", now, auditCutoffCap, e => e.Sequence),
            Target(db.WebhookCaptures, retention.WebhookCaptureDays, "WebhookCaptures", now),
            Target(db.CallLogs, retention.CallLogDays, "CallLogs", now),
            Target(db.StatusPageViews, retention.StatusPageViewDays, "StatusPageViews", now),
            Target(db.IncidentTimelineEvents, retention.IncidentTimelineEventDays, "IncidentTimelineEvents", now)
        ];

        var failures = await SweepAsync(targets, logger, ct);

        // Pruning the oldest audit entries leaves the hash chain starting partway in; unless that is
        // recorded, the next verification reads it as missing entries.
        if (retention.AuditLogDays > 0 && failures.All(f => f.Table != "AuditLogs"))
        {
            try
            {
                await scope.ServiceProvider
                    .GetRequiredService<Audit.AuditChainService>()
                    .NotePrunedAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retention pruned AuditLogs but could not record it on the audit chain");
            }
        }

        if (failures.Count == 0)
            return;

        throw new AggregateException(
            $"Retention prune failed for {failures.Count} table(s): {string.Join(", ", failures.Select(f => f.Table))}",
            failures.Select(f => f.Error));
    }

    private RetentionTarget Target<T>(
        DbSet<T> set, int days, string table, DateTime now,
        DateTime? cutoffCap = null,
        Expression<Func<T, object?>>? oldestFirst = null)
        where T : BaseEntity
        => new(table, days, ct => PruneTableAsync(
            set,
            days,
            now,
            ct,
            cutoffCap: cutoffCap,
            oldestFirst: oldestFirst,
            onBatch: total => logger.LogInformation(
                "Retention prune {Table}: {Count} row(s) removed so far", table, total)));

    internal static async Task<IReadOnlyList<(string Table, Exception Error)>> SweepAsync(
        IReadOnlyList<RetentionTarget> targets,
        ILogger logger,
        CancellationToken ct)
    {
        List<(string Table, Exception Error)> failures = [];

        foreach (var target in targets)
        {
            if (target.Days <= 0)
                continue;

            try
            {
                var deleted = await target.PruneAsync(ct);

                if (deleted > 0)
                    logger.LogInformation(
                        "Retention prune removed {Count} row(s) from {Table} older than {Days} day(s)",
                        deleted, target.Table, target.Days);

                if (deleted >= DefaultMaxRowsPerTable)
                    logger.LogWarning(
                        "Retention prune hit the per-run cap of {Cap} row(s) on {Table}; the rest is left for the next run",
                        DefaultMaxRowsPerTable, target.Table);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retention prune failed for {Table}; continuing with the remaining tables", target.Table);
                failures.Add((target.Table, ex));
            }
        }

        return failures;
    }

    internal static async Task<int> PruneTableAsync<T>(
        DbSet<T> set,
        int days,
        DateTime now,
        CancellationToken ct,
        int batchSize = DefaultBatchSize,
        int maxRows = DefaultMaxRowsPerTable,
        DateTime? cutoffCap = null,
        Expression<Func<T, object?>>? oldestFirst = null,
        Action<int>? onBatch = null)
        where T : BaseEntity
    {
        if (days <= 0)
            return 0;

        var cutoff = now.AddDays(-days);
        if (cutoffCap is { } cap && cap < cutoff)
            cutoff = cap;
        var total = 0;

        while (total < maxRows)
        {
            var take = Math.Min(batchSize, maxRows - total);

            // Unordered on purpose: CreatedAt is unindexed on three of the pruned tables. A caller
            // that needs whole batches removed from one end says so and pays for the sort.
            var eligible = set
                .IgnoreQueryFilters()
                .Where(e => e.CreatedAt < cutoff);

            if (oldestFirst is not null)
                eligible = eligible.OrderBy(oldestFirst);

            var ids = await eligible
                .Select(e => e.Id)
                .Take(take)
                .ToListAsync(ct);

            if (ids.Count == 0)
                break;

            var deleted = await set
                .IgnoreQueryFilters()
                .Where(e => ids.Contains(e.Id))
                .ExecuteDeleteAsync(ct);

            if (deleted == 0)
                break;

            total += deleted;
            onBatch?.Invoke(total);
        }

        return total;
    }
}
