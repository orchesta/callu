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

    /// <summary>
    /// Force the next escalation step to fire on the next poll tick. Used by the
    /// "Escalate Now" button — distinct from <see cref="TriggerEscalationAsync"/>
    /// which RESETS the step pointer to 0 (re-pages everyone). Advance leaves
    /// the policy + current step intact and only nudges <c>LastEscalationStepAt</c>
    /// so <c>ShouldTriggerStep</c> evaluates true on the next sweep. Returns true
    /// when the advance was applied, false when the incident has no active
    /// escalation to advance.
    /// </summary>
    Task<bool> AdvanceEscalationAsync(Guid incidentId, CancellationToken cancellationToken = default);
}
