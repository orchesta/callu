using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Plugins;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>
/// Sweeps <see cref="WebhookDelivery"/> rows whose <c>Status = 'Retrying'</c> and
/// <c>NextRetryAt &lt;= now</c>, re-fires the underlying ACK callback via
/// <see cref="IIncidentEventDispatcher"/>, and lets the dispatcher write a fresh
/// row with the new outcome. Stops at 6 attempts (terminal Failed).
/// </summary>
[DisallowConcurrentExecution]
public sealed class WebhookDeliveryRetryQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookDeliveryRetryQuartzJob> logger)
    : IJob
{
    private const int BatchSize = 50;

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var deliveryRepo = scope.ServiceProvider.GetRequiredService<IRepository<WebhookDelivery>>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IIncidentEventDispatcher>();

            var now = DateTime.UtcNow;
            var dueRows = await deliveryRepo.GetQueryable()
                .Where(d => d.Status == WebhookDeliveryStatus.Retrying && d.NextRetryAt != null && d.NextRetryAt <= now)
                .OrderBy(d => d.NextRetryAt)
                .Take(BatchSize)
                .ToListAsync(context.CancellationToken);

            if (dueRows.Count == 0) return;

            logger.LogInformation("WebhookDeliveryRetry processing {Count} rows", dueRows.Count);

            foreach (var row in dueRows)
            {
                row.Status = WebhookDeliveryStatus.Pending;
                row.NextRetryAt = null;
                row.UpdatedAt = now;
                deliveryRepo.Update(row);
            }
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.SaveChangesAsync(context.CancellationToken);

            var requeued = false;
            foreach (var row in dueRows)
            {
                if (string.IsNullOrEmpty(row.AckType)) continue;
                try
                {
                    await dispatcher.SendServiceAckAsync(row.IncidentId, row.AckType, context.CancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Retry dispatch failed for incident {IncidentId}", row.IncidentId);
                    row.Status = WebhookDeliveryStatus.Retrying;
                    row.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
                    deliveryRepo.Update(row);
                    requeued = true;
                }
            }

            if (requeued)
                await uow.SaveChangesAsync(context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "WebhookDeliveryRetryQuartzJob failed");
            throw;
        }
    }
}
