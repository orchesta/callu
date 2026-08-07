using Callu.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Re-fires outbound notification-channel deliveries whose backoff has elapsed, delegating the
/// re-send and state transition to <see cref="INotificationChannelService.ProcessDueRetriesAsync"/>.</summary>
[DisallowConcurrentExecution]
public sealed class NotificationChannelDeliveryRetryQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationChannelDeliveryRetryQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<INotificationChannelService>();
            await service.ProcessDueRetriesAsync(context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "NotificationChannelDeliveryRetryQuartzJob failed");
            throw;
        }
    }
}
