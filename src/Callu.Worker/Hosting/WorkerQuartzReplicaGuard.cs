using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Callu.Worker.Hosting;

/// <summary>
/// When Quartz is in-memory, at most one Worker should run. This posts a Redis TTL lease so a
/// second replica can warn — startup copy already covers the no-Redis case.
/// </summary>
public sealed class WorkerQuartzReplicaGuard(
    IServiceProvider services,
    ILogger<WorkerQuartzReplicaGuard> logger) : BackgroundService
{
    public const string LeaseKey = "callu:worker:quartz-lease";
    public static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan WarnCooldown = TimeSpan.FromMinutes(5);

    private readonly string _instanceId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    private DateTimeOffset? _lastWarnedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var multiplexer = services.GetService<IConnectionMultiplexer>();
        if (multiplexer is null)
            return;

        var db = multiplexer.GetDatabase();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await WorkerQuartzLease.RenewAsync(db, _instanceId, LeaseTtl);
                    if (!result.HeldByUs && result.OtherHolder is not null)
                        WarnAboutSecondReplica(result.OtherHolder);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Worker Quartz lease check failed");
                }

                await Task.Delay(RefreshInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected on shutdown
        }
        finally
        {
            try
            {
                await WorkerQuartzLease.ReleaseIfOwnedAsync(db, _instanceId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Worker Quartz lease release skipped");
            }
        }
    }

    private void WarnAboutSecondReplica(string otherHolder)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastWarnedAt is not null && now - _lastWarnedAt < WarnCooldown)
            return;

        _lastWarnedAt = now;
        logger.LogWarning(
            "Another Worker already holds the in-memory Quartz lease ({Holder}). " +
            "With Quartz:UsePersistentStore=false every replica fires every job independently — " +
            "escalations page twice, retries double-send. Run exactly one Worker, or set " +
            "Quartz:UsePersistentStore=true and create the qrtz_* tables.",
            otherHolder);
    }
}

/// <summary>Redis SET NX + TTL lease used by <see cref="WorkerQuartzReplicaGuard"/>.</summary>
public static class WorkerQuartzLease
{
    public static async Task<WorkerQuartzLeaseResult> RenewAsync(
        IDatabase db, string instanceId, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var holder = await db.StringGetAsync(WorkerQuartzReplicaGuard.LeaseKey);
        if (holder == instanceId)
        {
            await db.KeyExpireAsync(WorkerQuartzReplicaGuard.LeaseKey, ttl);
            return new WorkerQuartzLeaseResult(true, null);
        }

        if (!holder.HasValue)
        {
            var acquired = await db.StringSetAsync(
                WorkerQuartzReplicaGuard.LeaseKey, instanceId, ttl, When.NotExists);
            if (acquired)
                return new WorkerQuartzLeaseResult(true, null);

            holder = await db.StringGetAsync(WorkerQuartzReplicaGuard.LeaseKey);
            if (holder == instanceId)
                return new WorkerQuartzLeaseResult(true, null);

            return new WorkerQuartzLeaseResult(false, holder.HasValue ? (string?)holder : "unknown");
        }

        return new WorkerQuartzLeaseResult(false, (string?)holder);
    }

    public static async Task ReleaseIfOwnedAsync(IDatabase db, string instanceId)
    {
        ArgumentNullException.ThrowIfNull(db);
        var holder = await db.StringGetAsync(WorkerQuartzReplicaGuard.LeaseKey);
        if (holder == instanceId)
            await db.KeyDeleteAsync(WorkerQuartzReplicaGuard.LeaseKey);
    }
}

public sealed record WorkerQuartzLeaseResult(bool HeldByUs, string? OtherHolder);
