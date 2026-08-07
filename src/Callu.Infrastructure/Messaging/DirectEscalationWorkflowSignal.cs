using Callu.Application.Messaging;
using Callu.Application.Services;

namespace Callu.Infrastructure.Messaging;

/// <summary>In-process (no broker) signal that defers the orchestrator call until after the staging
/// transaction commits.</summary>
public sealed class DirectEscalationWorkflowSignal(IEscalationOrchestrator orchestrator)
    : IEscalationWorkflowSignal
{
    public Task<IEscalationDispatchHandle> StageForNewIncidentAsync(
        Guid incidentId,
        Guid escalationPolicyId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IEscalationDispatchHandle>(
            new DeferredOrchestratorHandle(orchestrator, incidentId, escalationPolicyId));

    private sealed class DeferredOrchestratorHandle(
        IEscalationOrchestrator orchestrator,
        Guid incidentId,
        Guid policyId) : IEscalationDispatchHandle
    {
        public Task DispatchAsync(CancellationToken cancellationToken = default) =>
            orchestrator.TriggerEscalationAsync(incidentId, policyId, cancellationToken);
    }
}
