using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>
/// Daily maintenance sweep that hard-deletes expired refresh tokens. Without it the
/// RefreshTokens table grows unbounded — every login + rotation leaves rows behind and
/// nothing ever removes them. Only expired tokens are deleted; revoked-but-unexpired
/// tokens stay until they expire so family theft-detection still fires on replay.
///
/// Idempotent: a second run simply finds fewer (or zero) expired rows.
/// </summary>
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
