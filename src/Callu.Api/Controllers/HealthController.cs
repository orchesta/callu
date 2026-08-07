using Asp.Versioning;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Health;
using Callu.Shared;
using Microsoft.AspNetCore.Authorization;

namespace Callu.Api.Controllers;

/// <summary>
/// Health check endpoints for monitoring
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[AllowAnonymous]
public class HealthController(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    IDistributedCache distributedCache,
    IServiceProvider serviceProvider,
    ReadinessSnapshotCache readinessCache,
    ILogger<HealthController> logger) : ControllerBase
{
    /// <summary>
    /// Liveness check — API is running
    /// </summary>
    [HttpGet]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Readiness check — API can reach the database, cache, and (if configured) message broker.
    /// </summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var snapshot = await readinessCache.GetAsync(async () =>
            new ReadinessSnapshotCache.Snapshot(
                await ProbeDatabaseAsync(ct),
                await ProbeCacheAsync(ct),
                await ProbeBrokerAsync(ct)),
            ct);

        var dbOk = snapshot.Database;
        var cacheOk = snapshot.Cache;
        var brokerOk = snapshot.Broker;

        var overall = dbOk && cacheOk && (brokerOk ?? true);
        var status = overall ? "Ready" : "Unhealthy";
        var timestamp = DateTime.UtcNow;

        var includeDetail = User.Identity?.IsAuthenticated == true && User.IsInRole("Admin");

        // SemVer + InformationalVersion (often +git SHA) only for Admin — anonymous liveness stays
        // fingerprint-light. The SHA answers "which republish am I on?", which is worth having behind auth.
        var build = includeDetail ? BuildIdentity.Current : null;

        object payload = includeDetail
            ? new
            {
                status,
                database = dbOk ? "Connected" : "Disconnected",
                cache = cacheOk ? "Connected" : "Disconnected",
                broker = brokerOk switch
                {
                    true => "Connected",
                    false => "Disconnected",
                    null => "NotConfigured"
                },
                version = build!.Version,
                informationalVersion = build.InformationalVersion,
                timestamp
            }
            : new
            {
                status,
                timestamp
            };

        return overall ? Ok(payload) : StatusCode(503, payload);
    }

    private async Task<bool> ProbeDatabaseAsync(CancellationToken ct)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);
            return await context.Database.CanConnectAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health probe: database unreachable");
            return false;
        }
    }

    private async Task<bool> ProbeCacheAsync(CancellationToken ct)
    {
        const string probeKey = "__callu:health:probe";
        try
        {
            await distributedCache.SetAsync(
                probeKey,
                [0x1],
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) },
                ct);
            _ = await distributedCache.GetAsync(probeKey, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health probe: cache unreachable");
            return false;
        }
    }

    /// <summary>
    /// Broker reachability, or null when no broker is configured.
    /// </summary>
    private async Task<bool?> ProbeBrokerAsync(CancellationToken ct)
    {
        var configured = serviceProvider
            .GetService<Callu.Infrastructure.Messaging.Broker.ICalluBrokerConnection>() is not null;
        if (!configured) return null;

        var health = serviceProvider.GetService<HealthCheckService>();
        if (health is null) return null;

        var report = await health.CheckHealthAsync(r => r.Tags.Contains("broker"), ct);
        if (report.Entries.Count == 0) return null;

        return report.Status == HealthStatus.Healthy;
    }
}
