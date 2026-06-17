using Callu.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

[DisallowConcurrentExecution]
public sealed class NotificationQueueQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationQueueQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            await dispatcher.ProcessNotificationQueueAsync(context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Quartz notification queue job failed");
            throw;
        }
    }
}
