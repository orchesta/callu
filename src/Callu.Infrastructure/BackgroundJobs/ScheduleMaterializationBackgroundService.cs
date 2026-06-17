using Callu.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.BackgroundJobs;

/// <summary>
/// API-side safety net for schedule rematerialization. Mirrors the Worker's
/// ScheduleMaterializationQuartzJob (cron 0 0 3 * * ?) so a single-process API
/// deployment (Callu:EnableBackgroundServices=true, no Worker) keeps extending
/// the 30-day horizon. When both API and Worker run with this enabled the
/// per-schedule advisory lock in ScheduleMaterializer serialises the two — one
/// host wins each schedule, the other no-ops.
/// </summary>
public sealed class ScheduleMaterializationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduleMaterializationBackgroundService> logger,
    TimeProvider time)
    : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _executionGuard = new(1, 1);
    private DateTimeOffset _nextRunUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _nextRunUtc = NextRunSlot(time.GetUtcNow());
        logger.LogInformation(
            "Schedule materialization background service started. First run at {NextRun}",
            _nextRunUtc);

        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (time.GetUtcNow() < _nextRunUtc) continue;

                if (!_executionGuard.Wait(0))
                {
                    logger.LogWarning("Previous materialization tick still running, skipping");
                    continue;
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var materializer = scope.ServiceProvider.GetRequiredService<IScheduleMaterializer>();
                    await materializer.RematerializeAllAsync(
                        IScheduleMaterializer.DefaultHorizon,
                        stoppingToken);
                    _nextRunUtc = NextRunSlot(time.GetUtcNow().AddMinutes(1));
                    logger.LogInformation(
                        "Schedule materialization completed. Next run at {NextRun}",
                        _nextRunUtc);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Schedule materialization failed; retrying next tick");
                }
                finally
                {
                    _executionGuard.Release();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Next 03:00 UTC slot strictly in the future relative to <paramref name="from"/>.
    /// Matches the Worker Quartz cron 0 0 3 * * ? so when both hosts run, they
    /// race for the same per-schedule advisory lock and only one materialize
    /// actually executes per row.
    /// </summary>
    private static DateTimeOffset NextRunSlot(DateTimeOffset from)
    {
        var today03 = new DateTimeOffset(from.Year, from.Month, from.Day, 3, 0, 0, TimeSpan.Zero);
        return from < today03 ? today03 : today03.AddDays(1);
    }
}
