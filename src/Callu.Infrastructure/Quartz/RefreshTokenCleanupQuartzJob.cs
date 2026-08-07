using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Daily sweep that hard-deletes expired refresh tokens; revoked-but-unexpired ones stay so
/// family theft-detection still fires on replay.</summary>
[DisallowConcurrentExecution]
public sealed class RefreshTokenCleanupQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RefreshTokenCleanupQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

            var deleted = await repo.DeleteExpiredAsync(DateTime.UtcNow, context.CancellationToken);
            if (deleted > 0)
                logger.LogInformation("Refresh-token cleanup removed {Count} expired token(s)", deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RefreshTokenCleanupQuartzJob failed");
            throw;
        }
    }
}
