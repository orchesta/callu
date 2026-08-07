using Callu.Application.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Callu.Infrastructure.Health;

/// <summary>Active TCP probe to the configured SMTP relay, cached briefly; Degraded when unconfigured.</summary>
public class SmtpHealthCheck(IEmailService emailService, HybridCache cache) : IHealthCheck
{
    private const string CacheKey = "health:smtp";
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var (ok, message) = await cache.GetOrCreateAsync(
            CacheKey,
            async ct => await emailService.ProbeAsync(ct),
            CacheOptions,
            cancellationToken: cancellationToken);

        if (ok)
            return HealthCheckResult.Healthy(message);

        if (message.StartsWith("SMTP not configured", StringComparison.Ordinal))
            return HealthCheckResult.Degraded(message);

        return HealthCheckResult.Unhealthy(message);
    }
}
