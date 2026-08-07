using Callu.Domain.Enums;

namespace Callu.Application.Services;

/// <summary>Propagates a status change down a service's dependent graph, derating by each edge's
/// <see cref="DependencyCriticality"/>. Only ever worsens; recovery is operator-driven.</summary>
public interface IServiceStatusCascadeEngine
{
    /// <summary>BFS over edges where <c>DependsOnServiceId == source</c>, applying each derived status only when
    /// strictly worse. Returns one outcome per service updated; opening the incidents is the caller's job.</summary>
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
