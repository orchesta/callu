namespace Callu.Application.Messaging;

/// <summary>Starts escalation for a newly created incident — either in-process or via message bus, in two
/// phases so neither mode can page before the incident row is committed.</summary>
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

/// <summary>Post-commit half of the two-phase escalation signal.</summary>
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
