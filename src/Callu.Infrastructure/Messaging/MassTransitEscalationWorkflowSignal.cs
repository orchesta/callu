using System.Diagnostics;
using Callu.Application.Contracts.Messages;
using Callu.Application.Messaging;
using MassTransit;

namespace Callu.Infrastructure.Messaging;

/// <summary>
/// Broker-mode signal. The <see cref="Publish"/> call runs INSIDE the caller's
/// transaction so MassTransit's <c>UseBusOutbox</c> diverts the message into the
/// <c>OutboxMessage</c> table (committed atomically with the incident row). The
/// MassTransit sweeper then forwards to RabbitMQ post-commit, off the request path.
/// </summary>
public sealed class MassTransitEscalationWorkflowSignal(IPublishEndpoint publishEndpoint)
    : IEscalationWorkflowSignal
{
    public async Task<IEscalationDispatchHandle> StageForNewIncidentAsync(
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

        await publishEndpoint.Publish(
            new TriggerIncidentEscalation(incidentId, escalationPolicyId),
            cancellationToken);

        return NoOpEscalationDispatchHandle.Instance;
    }
}
