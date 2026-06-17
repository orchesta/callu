using Callu.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.BackgroundJobs;

/// <summary>
/// Runs status-page HTTP health checks on a fixed interval.
/// </summary>
public sealed class HealthCheckBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<HealthCheckBackgroundService> logger)
    : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim _executionGuard = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Health check background service started");

        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!_executionGuard.Wait(0))
                {
                    logger.LogWarning("Previous health check tick is still running, skipping");
                    continue;
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var executor = scope.ServiceProvider.GetRequiredService<IHealthCheckExecutor>();
                    await executor.ExecuteAllChecksAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Error executing health checks");
                }
                finally
                {
                    _executionGuard.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        logger.LogInformation("Health check background service stopped");
    }

    public override void Dispose()
    {
        _executionGuard.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
