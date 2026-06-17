namespace Callu.Application.Messaging;

/// <summary>
/// Starts escalation for a newly created incident — either in-process or via message bus.
/// Two-phase contract so the broker-mode publish can go through MassTransit's
/// <c>UseBusOutbox</c> (publish inside transaction → diverted to OutboxMessage) while
/// the direct in-process call defers to after the transaction commits (no orphan
/// notifications on rollback).
/// </summary>
public interface IEscalationWorkflowSignal
{
    /// <summary>
    /// Stage the escalation trigger. MUST be called inside an open DbContext
    /// transaction. Returns a handle that the caller invokes after commit succeeds.
    /// </summary>
    Task<IEscalationDispatchHandle> StageForNewIncidentAsync(
        Guid incidentId,
        Guid escalationPolicyId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Post-commit half of the two-phase escalation signal. Broker-mode implementations
/// return a no-op (the publish was already staged into OutboxMessage); direct-mode
/// implementations invoke the orchestrator from here so an in-process trigger never
/// fires before the incident row is durably committed.
/// </summary>
public interface IEscalationDispatchHandle
{
    /// <summary>Invoked exactly once after the staging transaction has committed.</summary>
    Task DispatchAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Used when no escalation policy applies (incident has no team / no active policy).
/// Safe to call multiple times; does nothing.
/// </summary>
public sealed class NoOpEscalationDispatchHandle : IEscalationDispatchHandle
{
    public static readonly NoOpEscalationDispatchHandle Instance = new();
    private NoOpEscalationDispatchHandle() { }
    public Task DispatchAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
