namespace Callu.Application.Services;

/// <summary>
/// Orchestrates incident escalation through policy steps
/// </summary>
public interface IEscalationOrchestrator
{
    /// <summary>
    /// Process all pending escalations (called by background service)
    /// </summary>
    Task ProcessPendingEscalationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Trigger escalation for a specific incident
    /// </summary>
    Task TriggerEscalationAsync(Guid incidentId, Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel escalation for an incident (e.g., when resolved)
    /// </summary>
    Task CancelEscalationAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>Force the next escalation step to fire on the next poll tick, leaving the policy and current
    /// step pointer intact. False when the incident has no active escalation to advance.</summary>
    Task<bool> AdvanceEscalationAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>Page the incident's next escalation step now, without waiting for that step's delay or the next
    /// sweep tick. Escalation only moves forwards, and true means somebody was actually PAGED.</summary>
    Task<bool> EscalateNowAsync(Guid incidentId, CancellationToken cancellationToken = default);
}
