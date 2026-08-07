using Callu.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

[DisallowConcurrentExecution]
public sealed class HealthCheckQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<HealthCheckQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IHealthCheckExecutor>();
            await executor.ExecuteAllChecksAsync(context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Quartz health check job failed");
            throw;
        }
    }
}
