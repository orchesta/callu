using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// A component on a status page representing a service or subsystem
/// </summary>
public class StatusPageComponent : BaseEntity
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Component status: operational, degraded, partial_outage, major_outage, maintenance
    /// </summary>
    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "operational";

    /// <summary>
    /// Display order on the page
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Optional link to an internal Service entity for auto-sync
    /// </summary>
    public Guid? ServiceId { get; set; }

    public bool HealthCheckEnabled { get; set; } = false;

    [StringLength(2000)]
    public string? HealthCheckUrl { get; set; }

    [StringLength(10)]
    public string? HealthCheckHttpMethod { get; set; }

    public int HealthCheckIntervalSeconds { get; set; } = 60;
    public int HealthCheckTimeoutSeconds { get; set; } = 10;

    [StringLength(2000)]
    public string? HealthCheckHeaders { get; set; }

    [StringLength(10_000)]
    public string? HealthCheckBody { get; set; }

    [StringLength(100)]
    public string? HealthCheckContentType { get; set; }

    [StringLength(10_000)]
    public string? HealthCheckFieldMappings { get; set; }

    [StringLength(5_000)]
    public string? HealthCheckStateMapping { get; set; }

    [StringLength(65_536)]
    public string? HealthCheckSampleResponse { get; set; }

    public DateTime? LastHealthCheckAt { get; set; }

    [StringLength(50)]
    public string? LastHealthCheckResult { get; set; }

    public int? LastHealthCheckResponseMs { get; set; }
    public int HealthCheckConsecutiveFailures { get; set; } = 0;

    /// <summary>
    /// Consecutive failing probes required before the component visibly transitions to a down
    /// status (flap damping). Recovery to Operational is immediate. Null → default (3). (CAS-2)
    /// </summary>
    public int? HealthCheckFailureThreshold { get; set; }

    public bool HealthCheckListeningMode { get; set; } = false;

    public Guid StatusPageId { get; set; }
    public virtual StatusPage StatusPage { get; set; } = null!;
}
