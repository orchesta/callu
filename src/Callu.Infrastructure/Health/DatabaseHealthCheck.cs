using Microsoft.Extensions.Diagnostics.HealthChecks;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Callu.Infrastructure.Health;

/// <summary>
/// Health check that verifies PostgreSQL database connectivity
/// using the existing ApplicationDbContext.
/// </summary>
public class DatabaseHealthCheck(ApplicationDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL connection is healthy.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL connection failed.",
                exception: ex);
        }
    }
}
