using Asp.Versioning;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Callu.Infrastructure.Persistence;

namespace Callu.Api.Controllers;

/// <summary>
/// Health check endpoints for monitoring
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public class HealthController(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    IDistributedCache distributedCache,
    IServiceProvider serviceProvider,
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
            timestamp = DateTime.UtcNow,
            version = typeof(HealthController).Assembly.GetName().Version?.ToString() ?? "1.0.0"
        });
    }

    /// <summary>
    /// Readiness check — API can reach the database, cache, and (if configured) message broker.
    /// </summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var dbOk = await ProbeDatabaseAsync(ct);
        var cacheOk = await ProbeCacheAsync(ct);
        var brokerOk = ProbeBroker();

        var overall = dbOk && cacheOk && (brokerOk ?? true);

        var payload = new
        {
            status = overall ? "Ready" : "Unhealthy",
            database = dbOk ? "Connected" : "Disconnected",
            cache = cacheOk ? "Connected" : "Disconnected",
            broker = brokerOk switch
            {
                true => "Connected",
                false => "Disconnected",
                null => "NotConfigured"
            },
            timestamp = DateTime.UtcNow
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

    private bool? ProbeBroker()
    {
        var bus = serviceProvider.GetService<IBus>();
        return bus is null ? null : true;
    }
}
