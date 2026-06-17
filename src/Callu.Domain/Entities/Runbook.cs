using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Operational runbook with step-by-step procedures for responding to incidents.
/// Can be linked to a specific service.
/// </summary>
public class Runbook : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Rich-text content (Markdown)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional link to a specific service
    /// </summary>
    public Guid? ServiceId { get; set; }
    public Service? Service { get; set; }

    /// <summary>
    /// Tags for categorization (JSON array of strings)
    /// </summary>
    public string TagsJson { get; set; } = "[]";

    /// <summary>
    /// Author user ID
    /// </summary>
    [MaxLength(450)]
    public string AuthorId { get; set; } = string.Empty;

    /// <summary>
    /// Last time someone used/executed this runbook
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Usage count
    /// </summary>
    public int UsageCount { get; set; }
}
