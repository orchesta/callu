using System.Diagnostics;
using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Contracts.Messages;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Messaging.Consuming;

/// <summary>Handles one wire message type. Selected by an allow-list, never by reflection over the wire value.</summary>
public interface ICalluMessageHandler
{
    string WireName { get; }

    Task HandleAsync(string payload, CancellationToken cancellationToken);

    /// <summary>Called once when the ladder is exhausted, to leave a record where an operator looks.</summary>
    Task ReportPermanentFailureAsync(string payload, string reason, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class TriggerIncidentEscalationHandler(
    IEscalationOrchestrator orchestrator,
    IIncidentTimelineEventRepository timelineRepo,
    IIncidentRepository incidentRepo,
    ITransactionManager transactionManager,
    IAuditLogService auditLogService,
    CalluMetrics metrics,
    ILogger<TriggerIncidentEscalationHandler> logger) : ICalluMessageHandler
{
    public string WireName => CalluTopology.TriggerIncidentEscalationMessage;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = CalluMessagePayload.Deserialize<TriggerIncidentEscalation>(payload);

        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("incident.id", message.IncidentId.ToString());
            activity.SetTag("escalation.policy.id", message.EscalationPolicyId.ToString());
        }

        logger.LogInformation(
            "TriggerIncidentEscalation: incident {IncidentId}, policy {PolicyId}",
            message.IncidentId, message.EscalationPolicyId);

        var sw = Stopwatch.StartNew();
        try
        {
            await orchestrator.TriggerEscalationAsync(
                message.IncidentId, message.EscalationPolicyId, cancellationToken);
        }
        finally
        {
            metrics.RecordEscalationDuration(sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>Neither write may throw: this is the last line of reporting for a page that never happened.</summary>
    public async Task ReportPermanentFailureAsync(string payload, string reason, CancellationToken cancellationToken)
    {
        var message = CalluMessagePayload.Deserialize<TriggerIncidentEscalation>(payload);

        logger.LogError(
            "Escalation was never staged for incident {IncidentId} (policy {PolicyId}): {Reason}. "
            + "Nobody is being paged until the reconcile sweep picks it up.",
            message.IncidentId, message.EscalationPolicyId, reason);

        metrics.EscalationTriggerDeadLettered();

        try
        {
            await auditLogService.LogAsync(
                userId: null,
                action: AuditAction.EscalationTriggerFailed,
                entityName: "Incident",
                entityId: message.IncidentId.ToString(),
                newValues: reason,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not write the audit row for the failed escalation trigger on {IncidentId}", message.IncidentId);
        }

        try
        {
            await transactionManager.ExecuteInTransactionAsync(async () =>
            {
                // The incident may have been resolved and hard-deleted meanwhile, and the timeline row
                // carries a foreign key.
                var exists = await incidentRepo.ExistsAsync(i => i.Id == message.IncidentId, cancellationToken);
                if (!exists) return;

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = message.IncidentId,
                    EventType = TimelineEventType.Escalated,
                    Title = "Escalation did not start",
                    Description =
                        "The escalation trigger failed after every retry, so no step was staged and paging "
                        + $"did not begin. Reason: {reason}",
                    ActorUserId = "system",
                }, cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not write the timeline row for the failed escalation trigger on {IncidentId}", message.IncidentId);
        }
    }
}

public sealed class NotifyStatusPageSubscribersHandler(
    IStatusPageSubscriberEmailSender sender,
    ILogger<NotifyStatusPageSubscribersHandler> logger) : ICalluMessageHandler
{
    public string WireName => CalluTopology.NotifyStatusPageSubscribersMessage;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = CalluMessagePayload.Deserialize<NotifyStatusPageSubscribers>(payload);

        logger.LogInformation("NotifyStatusPageSubscribers: incident {IncidentId}", message.StatusPageIncidentId);
        await sender.SendForIncidentAsync(message.StatusPageIncidentId, cancellationToken);
    }
}

internal static class CalluMessagePayload
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static TMessage Deserialize<TMessage>(string payload) =>
        JsonSerializer.Deserialize<TMessage>(payload, Options)
        ?? throw new InvalidOperationException($"Payload deserialized to null for {typeof(TMessage).Name}.");
}
