using Callu.Domain.Enums;

namespace Callu.Application.Services;

/// <summary>
/// Propagates a status change from one service down its dependent graph,
/// derating the downstream status by the edge's <see cref="DependencyCriticality"/>.
/// Only worsens; recovery is operator-driven (resolving the incident on the
/// source service clears it via the normal resolve path, not via auto-cascade).
///
/// </summary>
public interface IServiceStatusCascadeEngine
{
    /// <summary>
    /// BFS from <paramref name="sourceServiceId"/> across edges where
    /// <c>DependsOnServiceId == source</c> (i.e. "who depends on the source?"),
    /// deriving a new status for each downstream service from its edge criticality.
    /// Applies the derived status only when it is strictly worse than the
    /// target's current status. Returns one outcome per service actually updated,
    /// flagging which ones the operator asked to open an incident for. Incident
    /// creation itself is the caller's responsibility — done post-commit so the
    /// cascade's status writes and the incident's own escalation dispatch don't
    /// share a transaction.
    /// </summary>
    Task<IReadOnlyList<ServiceCascadeOutcome>> PropagateAsync(
        Guid sourceServiceId,
        ServiceStatus newSourceStatus,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One downstream service whose status the cascade worsened.
/// </summary>
public record ServiceCascadeOutcome(
    Guid ServiceId,
    string ServiceName,
    ServiceStatus NewStatus,
    bool ShouldCreateIncident);
