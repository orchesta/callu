using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents an audit log entry for system activity tracking
/// </summary>
public class AuditLog : BaseEntity
{
    /// <summary>
    /// User ID who performed the action
    /// </summary>
    [StringLength(128)]
    public string? UserId { get; set; }

    /// <summary>
    /// User display name (denormalized)
    /// </summary>
    [StringLength(100)]
    public string? UserName { get; set; }

    /// <summary>
    /// Type of action performed
    /// </summary>
    public AuditAction Action { get; set; }

    /// <summary>
    /// Entity type that was affected
    /// </summary>
    [Required]
    [StringLength(100)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Entity ID that was affected
    /// </summary>
    public Guid? EntityId { get; set; }

    /// <summary>
    /// Description of the action
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Old values as JSON (for updates)
    /// </summary>
    public string? OldValues { get; set; }

    /// <summary>
    /// New values as JSON (for creates/updates)
    /// </summary>
    public string? NewValues { get; set; }

    /// <summary>
    /// IP address of the user
    /// </summary>
    [StringLength(50)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string
    /// </summary>
    [StringLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Request path
    /// </summary>
    [StringLength(500)]
    public string? RequestPath { get; set; }
}
