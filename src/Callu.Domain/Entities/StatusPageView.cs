using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Tracks anonymous page views for public status pages
/// </summary>
public class StatusPageView : BaseEntity
{
    public Guid StatusPageId { get; set; }
    public virtual StatusPage StatusPage { get; set; } = null!;

    /// <summary>
    /// Hashed visitor IP for uniqueness counting (SHA256 first 16 chars)
    /// </summary>
    [StringLength(64)]
    public string? VisitorHash { get; set; }

    /// <summary>
    /// UTC timestamp of the page view
    /// </summary>
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}
