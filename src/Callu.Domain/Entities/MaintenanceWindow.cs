using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Scheduled maintenance window during which alerts are suppressed
/// for specific services.
/// </summary>
public class MaintenanceWindow : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// When the maintenance window starts (UTC)
    /// </summary>
    public DateTime StartsAt { get; set; }

    /// <summary>
    /// When the maintenance window ends (UTC)
    /// </summary>
    public DateTime EndsAt { get; set; }

    /// <summary>
    /// JSON array of affected service IDs. Ignored when
    /// <see cref="AppliesToAllServices"/> is true.
    /// </summary>
    public string AffectedServiceIdsJson { get; set; } = "[]";

    /// <summary>
    /// Explicit "this window suppresses every service" toggle, required when no service list is given.
    /// </summary>
    public bool AppliesToAllServices { get; set; }

    /// <summary>
    /// Whether to suppress all alerts or just auto-ack them
    /// </summary>
    public MaintenanceWindowMode Mode { get; set; } = MaintenanceWindowMode.SuppressAlerts;

    /// <summary>
    /// Creator user ID
    /// </summary>
    [MaxLength(450)]
    public string CreatedById { get; set; } = string.Empty;

    /// <summary>
    /// Whether this window has been manually cancelled
    /// </summary>
    public bool IsCancelled { get; set; }
}

public enum MaintenanceWindowMode
{
    /// <summary>Completely suppress alerts</summary>
    SuppressAlerts,
    /// <summary>Auto-acknowledge alerts but still create incidents</summary>
    AutoAcknowledge
}
