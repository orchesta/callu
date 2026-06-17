using Callu.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

[DisallowConcurrentExecution]
public sealed class EscalationProcessingQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<EscalationProcessingQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IEscalationOrchestrator>();
            await orchestrator.ProcessPendingEscalationsAsync(context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Quartz escalation job failed");
            throw;
        }
    }
}
