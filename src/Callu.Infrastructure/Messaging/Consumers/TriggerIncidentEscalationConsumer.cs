using Callu.Application.Contracts.Messages;
using System.Diagnostics;
using Callu.Application.Services;
using Callu.Infrastructure.Telemetry;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Messaging.Consumers;

public sealed class TriggerIncidentEscalationConsumer(
    IEscalationOrchestrator orchestrator,
    CalluMetrics metrics,
    ILogger<TriggerIncidentEscalationConsumer> logger)
    : IConsumer<TriggerIncidentEscalation>
{
    public async Task Consume(ConsumeContext<TriggerIncidentEscalation> context)
    {
        var m = context.Message;

        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("incident.id", m.IncidentId.ToString());
            activity.SetTag("escalation.policy.id", m.EscalationPolicyId.ToString());
            activity.SetTag("messaging.message_id", context.MessageId?.ToString());
        }

        logger.LogInformation(
            "TriggerIncidentEscalation: incident {IncidentId}, policy {PolicyId}",
            m.IncidentId, m.EscalationPolicyId);

        var sw = Stopwatch.StartNew();
        try
        {
            await orchestrator.TriggerEscalationAsync(
                m.IncidentId,
                m.EscalationPolicyId,
                context.CancellationToken);
        }
        finally
        {
            metrics.RecordEscalationDuration(sw.Elapsed.TotalMilliseconds);
        }
    }
}
