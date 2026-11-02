using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Publishes the public chain's head when Callu:AuditCheckpoint:PublishUrl is set.</summary>
[DisallowConcurrentExecution]
public sealed class AuditCheckpointQuartzJob(IServiceScopeFactory scopeFactory, ILogger<AuditCheckpointQuartzJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        using var correlation = AuditJobCorrelation.Begin(context);
        using var scope = scopeFactory.CreateScope();
        var checkpoints = scope.ServiceProvider.GetRequiredService<AuditCheckpointService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        AuditCheckpointPublishOutcome outcome;
        try
        {
            outcome = await checkpoints.PublishDueAsync(context.CancellationToken);
        }
        catch (Exception ex) when (!context.CancellationToken.IsCancellationRequested)
        {
            // Failing before any checkpoint row exists (no signer, a broken leaf run): still a row an auditor can find.
            logger.LogError(ex, "Audit checkpoint publication failed before a checkpoint was stored");
            await audit.LogAsync(null, AuditAction.PublishFailed, "AuditCheckpoint", Guid.Empty.ToString(),
                description: "Checkpoint publication failed before a checkpoint could be stored",
                cancellationToken: context.CancellationToken);
            return;
        }

        switch (outcome.Status)
        {
            case AuditCheckpointPublishStatus.Published:
                await audit.LogAsync(null, AuditAction.Created, "AuditCheckpoint", outcome.Checkpoint!.Id.ToString(),
                    newValues: $"headSequence={outcome.Checkpoint.HeadSequence}; anchor={outcome.Checkpoint.AnchorType}",
                    description: "Checkpoint published", cancellationToken: context.CancellationToken);
                break;
            case AuditCheckpointPublishStatus.IntegrityFault:
                // Not a quiet "nothing new": the leaves contradict the head, and every run will refuse until someone looks.
                logger.LogError("Audit checkpoint publication refused: the stored public chain is inconsistent");
                await audit.LogAsync(null, AuditAction.PublishFailed, "AuditCheckpoint", Guid.Empty.ToString(),
                    description: "Checkpoint publication refused: an integrity fault in the stored public chain",
                    cancellationToken: context.CancellationToken);
                break;
            case AuditCheckpointPublishStatus.Failed:
                await audit.LogAsync(null, AuditAction.PublishFailed, "AuditCheckpoint", outcome.Checkpoint!.Id.ToString(),
                    description: "Checkpoint publication failed; it stays pending and is retried on the next run",
                    cancellationToken: context.CancellationToken);
                break;
        }
    }
}
