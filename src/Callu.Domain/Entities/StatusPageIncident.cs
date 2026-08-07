using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// An incident reported on a status page
/// </summary>
public class StatusPageIncident : BaseEntity
{
    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Incident status: investigating, identified, monitoring, resolved
    /// </summary>
    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "investigating";

    /// <summary>
    /// Impact level: none, minor, major, critical
    /// </summary>
    [StringLength(50)]
    public string Impact { get; set; } = "minor";

    public Guid StatusPageId { get; set; }
    public virtual StatusPage StatusPage { get; set; } = null!;

    public virtual ICollection<StatusPageIncidentUpdate> Updates { get; set; } = new List<StatusPageIncidentUpdate>();
}
