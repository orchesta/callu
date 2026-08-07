using System.Diagnostics;
using Callu.Application.Contracts.Messages;
using Callu.Application.Messaging;

namespace Callu.Infrastructure.Messaging.Outbox;

/// <summary>Broker-mode signal that stages the trigger in the caller's transaction and nudges the dispatcher after it commits.</summary>
public sealed class OutboxEscalationWorkflowSignal(IOutboxWriter outbox, IOutboxNudge nudge)
    : IEscalationWorkflowSignal
{
    public Task<IEscalationDispatchHandle> StageForNewIncidentAsync(
        Guid incidentId,
        Guid escalationPolicyId,
        CancellationToken cancellationToken = default)
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("incident.id", incidentId.ToString());
            activity.SetTag("escalation.policy.id", escalationPolicyId.ToString());
        }

        outbox.Stage(
            CalluTopology.TriggerIncidentEscalationMessage,
            new TriggerIncidentEscalation(incidentId, escalationPolicyId));

        return Task.FromResult<IEscalationDispatchHandle>(new OutboxNudgeHandle(nudge));
    }

    /// <summary>Post-commit half: a wake-up only, so a nudge failure can never fail a committed incident.</summary>
    private sealed class OutboxNudgeHandle(IOutboxNudge nudge) : IEscalationDispatchHandle
    {
        public Task DispatchAsync(CancellationToken cancellationToken = default)
        {
            nudge.Nudge();
            return Task.CompletedTask;
        }
    }
}
