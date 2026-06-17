using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Postmortem document linked to an incident.
/// Captures root cause, timeline, follow-up action items, and lessons learned.
/// </summary>
public class Postmortem : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Rich-text body (Markdown formatted)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Brief summary of the root cause
    /// </summary>
    [MaxLength(2000)]
    public string? RootCause { get; set; }

    /// <summary>
    /// Linked incident
    /// </summary>
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;

    /// <summary>
    /// Author user ID
    /// </summary>
    [MaxLength(450)]
    public string AuthorId { get; set; } = string.Empty;

    /// <summary>
    /// Status: Draft → InReview → Published → Locked (one-way transitions).
    /// </summary>
    public PostmortemStatus Status { get; set; } = PostmortemStatus.Draft;

    /// <summary>
    /// When the postmortem was published
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// JSON array of follow-up action items
    /// </summary>
    public string ActionItemsJson { get; set; } = "[]";

    /// <summary>Draft → InReview. Author hands the document to reviewers.</summary>
    public void Submit()
    {
        if (Status != PostmortemStatus.Draft)
            throw new InvalidOperationException($"Only Draft postmortems can be submitted for review. Current: '{Status}'.");
        Status = PostmortemStatus.InReview;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>InReview → Draft. Reviewer sends it back.</summary>
    public void Reject()
    {
        if (Status != PostmortemStatus.InReview)
            throw new InvalidOperationException($"Only InReview postmortems can be rejected. Current: '{Status}'.");
        Status = PostmortemStatus.Draft;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Draft | InReview → Published. PublishedAt is stamped now.</summary>
    public void Publish()
    {
        if (Status is not (PostmortemStatus.Draft or PostmortemStatus.InReview))
            throw new InvalidOperationException($"Only Draft or InReview postmortems can be published. Current: '{Status}'.");
        Status = PostmortemStatus.Published;
        PublishedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Published → Locked. Final immutability — even action items frozen.</summary>
    public void Lock()
    {
        if (Status != PostmortemStatus.Published)
            throw new InvalidOperationException($"Only Published postmortems can be locked. Current: '{Status}'.");
        Status = PostmortemStatus.Locked;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mutability gate. Full edits allowed for Draft and InReview; only ActionItems
    /// mutable when Published; nothing mutable when Locked.
    /// </summary>
    public bool CanEditAllFields => Status is PostmortemStatus.Draft or PostmortemStatus.InReview;

    /// <summary>Action items continue to be completed post-publish but freeze on Lock.</summary>
    public bool CanEditActionItems => Status is not PostmortemStatus.Locked;
}

public enum PostmortemStatus
{
    Draft,
    InReview,
    Published,
    Locked
}
