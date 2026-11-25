using System.Text.Json.Nodes;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Shared.Models.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Publishes the public chain's head when Callu:AuditCheckpoint:PublishUrl is set.</summary>
[DisallowConcurrentExecution]
public sealed class AuditCheckpointQuartzJob(IServiceScopeFactory scopeFactory, ILogger<AuditCheckpointQuartzJob> logger) : IJob
{
    private const string ResourceType = "AuditCheckpoint";

    // No checkpoint exists yet to name. An empty GUID would be exported as a real id of zeros, whereas
    // an empty string is stored as no resource id at all.
    private const string NoCheckpoint = "";

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
            await audit.LogAsync(null, AuditAction.PublishFailed, ResourceType, NoCheckpoint,
                description: "Checkpoint publication failed before a checkpoint could be stored",
                cancellationToken: context.CancellationToken);
            return;
        }

        switch (outcome.Status)
        {
            case AuditCheckpointPublishStatus.Published:
                var published = outcome.Checkpoint!;
                await audit.LogAsync(new AuditRecord(null, AuditAction.Published, ResourceType, published.Id.ToString())
                {
                    NewValues = $"headSequence={published.HeadSequence}; anchor={published.AnchorType}",
                    Description = "Checkpoint published",
                    Details = new AuditDetails
                    {
                        Metadata = new JsonObject
                        {
                            ["checkpoint"] = new JsonObject
                            {
                                ["headSequence"] = published.HeadSequence,
                                ["eventCount"] = published.EventCount,
                                ["anchor"] = published.AnchorType,
                            },
                        },
                    },
                }, context.CancellationToken);
                break;
            case AuditCheckpointPublishStatus.IntegrityFault:
                // Not a quiet "nothing new": the leaves contradict the head, and every run will refuse until someone looks.
                logger.LogError("Audit checkpoint publication refused: the stored public chain is inconsistent");
                await audit.LogAsync(null, AuditAction.PublishFailed, ResourceType, NoCheckpoint,
                    description: "Checkpoint publication refused: an integrity fault in the stored public chain",
                    cancellationToken: context.CancellationToken);
                break;
            case AuditCheckpointPublishStatus.Failed:
                var failed = outcome.Checkpoint!;
                await audit.LogAsync(new AuditRecord(null, AuditAction.PublishFailed, ResourceType, failed.Id.ToString())
                {
                    Description = "Checkpoint publication failed; it stays pending and is retried on the next run",
                    Details = new AuditDetails
                    {
                        Metadata = new JsonObject
                        {
                            ["checkpoint"] = new JsonObject
                            {
                                ["headSequence"] = failed.HeadSequence,
                                ["anchor"] = failed.AnchorType,
                                ["attempts"] = failed.PublishAttempts,
                            },
                        },
                    },
                }, context.CancellationToken);
                break;
        }
    }
}
