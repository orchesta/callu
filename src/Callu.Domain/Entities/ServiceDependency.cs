using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents a dependency relationship between services
/// </summary>
public class ServiceDependency : BaseEntity
{
    /// <summary>
    /// The service that has the dependency
    /// </summary>
    public Guid ServiceId { get; set; }

    /// <summary>
    /// Navigation property for the service
    /// </summary>
    public virtual Service Service { get; set; } = null!;

    /// <summary>
    /// The service that is depended upon
    /// </summary>
    public Guid DependsOnServiceId { get; set; }

    /// <summary>
    /// Navigation property for the dependent service
    /// </summary>
    public virtual Service DependsOnService { get; set; } = null!;

    /// <summary>
    /// Type of dependency (Upstream, Downstream, Bidirectional)
    /// </summary>
    public DependencyType Type { get; set; } = DependencyType.Upstream;

    /// <summary>
    /// Criticality of this dependency
    /// </summary>
    public DependencyCriticality Criticality { get; set; } = DependencyCriticality.Medium;

    /// <summary>
    /// Description of the dependency
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Latency expectation in milliseconds (for monitoring)
    /// </summary>
    public int? ExpectedLatencyMs { get; set; }

    /// <summary>
    /// Should cascade status (if dependency is down, mark this service as affected)
    /// </summary>
    public bool CascadeStatus { get; set; } = true;

    /// <summary>
    /// Should create incident when dependency fails
    /// </summary>
    public bool CreateIncidentOnFailure { get; set; } = true;
}
