using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Callu.Infrastructure.Health;

/// <summary>Redis reachability probe, registered only when ConnectionStrings:Redis is set.</summary>
/// <remarks>Tagged "external": it runs on /health/detail but not on the container probe.</remarks>
public sealed class RedisHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await multiplexer.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy(
                $"Redis replied to PING in {latency.TotalMilliseconds:F0} ms.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Redis is configured but not usable. HybridCache L2, the SignalR backplane and the " +
                "Data Protection key ring are all degraded: cached reads fall back to the database, " +
                "real-time updates do not cross hosts, and stored SMTP / SIP passwords may not " +
                "decrypt. A NOAUTH error here means REDIS_PASSWORD is wrong or missing.",
                exception: ex);
        }
    }
}
