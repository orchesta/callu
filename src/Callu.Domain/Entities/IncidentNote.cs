using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents a note/comment on an incident
/// </summary>
public class IncidentNote : BaseEntity
{
    /// <summary>
    /// The incident this note belongs to
    /// </summary>
    public Guid IncidentId { get; set; }

    /// <summary>
    /// Navigation property for incident
    /// </summary>
    public virtual Incident Incident { get; set; } = null!;

    /// <summary>
    /// Note content
    /// </summary>
    [Required]
    [StringLength(4000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Is this an internal note (not visible to external users)
    /// </summary>
    public bool IsInternal { get; set; } = false;

    /// <summary>
    /// Is this note pinned to top
    /// </summary>
    public bool IsPinned { get; set; } = false;
}
