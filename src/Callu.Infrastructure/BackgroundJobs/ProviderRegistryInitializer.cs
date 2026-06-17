using Callu.Application.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.BackgroundJobs;

/// <summary>
/// Loads communication providers at startup (with retry), then periodically reloads so a
/// provider enabled/disabled via the API — a separate process whose in-process reload does
/// NOT reach this host — is picked up automatically here, without a restart. The registry
/// only logs at Information when the active set actually changes.
/// </summary>
public sealed class ProviderRegistryInitializer(
    IServiceProvider serviceProvider,
    ILogger<ProviderRegistryInitializer> logger)
    : BackgroundService
{
    private static readonly TimeSpan ReloadInterval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                logger.LogInformation("Loading communication providers (attempt {Attempt}/{Max})...", attempt, maxAttempts);
                await ReloadAsync(stoppingToken);
                logger.LogInformation("Communication providers loaded.");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load providers (attempt {Attempt}/{Max}), retrying in {Delay}s",
                    attempt, maxAttempts, delay.TotalSeconds);

                if (attempt >= maxAttempts)
                {
                    logger.LogWarning("Exhausted all {Max} attempts to load communication providers; " +
                        "will keep retrying on the periodic reload.", maxAttempts);
                    break;
                }

                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(1).Ticks));
            }
        }

        using var timer = new PeriodicTimer(ReloadInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ReloadAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Periodic communication-provider reload failed; retrying next tick");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var registry = serviceProvider.GetRequiredService<ICommunicationProviderRegistry>();
        await registry.ReloadProvidersAsync(cancellationToken);
    }
}
