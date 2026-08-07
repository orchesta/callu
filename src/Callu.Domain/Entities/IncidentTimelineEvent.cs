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

    /// <summary>Escalation step this event belongs to, when it is one.</summary>
    // Deliberately not a foreign key: a policy can be edited or a step deleted long after the page
    // went out, and the record of which step paged whom has to survive that.
    public Guid? EscalationStepId { get; set; }

    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 1000;

    private string _title = string.Empty;
    private string? _description;

    /// <summary>
    /// Event title, clamped to the column width.
    /// </summary>
    [Required]
    [StringLength(MaxTitleLength)]
    public string Title
    {
        get => _title;
        set => _title = Clamp(value, MaxTitleLength) ?? string.Empty;
    }

    /// <summary>
    /// Event description/details, clamped like <see cref="Notification.ErrorMessage"/>.
    /// </summary>
    [StringLength(MaxDescriptionLength)]
    public string? Description
    {
        get => _description;
        set => _description = Clamp(value, MaxDescriptionLength);
    }

    private static string? Clamp(string? value, int max)
    {
        if (value is null || value.Length <= max)
            return value;

        var cut = max;
        if (char.IsHighSurrogate(value[cut - 1]))
            cut--;

        return value[..cut];
    }

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
