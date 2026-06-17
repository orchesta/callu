using Callu.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodically processes incident escalations.
/// </summary>
public sealed class EscalationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<EscalationBackgroundService> logger)
    : BackgroundService
{
    private readonly TimeSpan _baseInterval = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _maxBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Escalation background service started");
        var currentDelay = _baseInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessEscalationsAsync(stoppingToken);
                currentDelay = _baseInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Escalation processing failed. Retrying in {Delay}s", currentDelay.TotalSeconds);
                currentDelay = TimeSpan.FromTicks(Math.Min(currentDelay.Ticks * 2, _maxBackoff.Ticks));
            }

            try
            {
                await Task.Delay(currentDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Escalation background service stopped");
    }

    private async Task ProcessEscalationsAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IEscalationOrchestrator>();
        await orchestrator.ProcessPendingEscalationsAsync(stoppingToken);
    }
}
