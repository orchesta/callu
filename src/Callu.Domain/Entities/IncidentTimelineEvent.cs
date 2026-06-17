using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents a timeline event for an incident
/// </summary>
public class IncidentTimelineEvent : BaseEntity
{
    /// <summary>
    /// The incident this event belongs to
    /// </summary>
    public Guid IncidentId { get; set; }

    /// <summary>
    /// Navigation property for incident
    /// </summary>
    public virtual Incident Incident { get; set; } = null!;

    /// <summary>
    /// Type of timeline event
    /// </summary>
    public TimelineEventType EventType { get; set; }

    /// <summary>
    /// Event title
    /// </summary>
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Event description/details
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// User who triggered this event (ID)
    /// </summary>
    [StringLength(128)]
    public string? ActorUserId { get; set; }

    /// <summary>
    /// Actor display name (denormalized for performance)
    /// </summary>
    [StringLength(100)]
    public string? ActorName { get; set; }

    /// <summary>
    /// Actor initials (denormalized for UI)
    /// </summary>
    [StringLength(10)]
    public string? ActorInitials { get; set; }

    /// <summary>
    /// Additional metadata as JSON
    /// </summary>
    [StringLength(4000)]
    public string? Metadata { get; set; }
}
