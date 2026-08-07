using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// An update to a status page incident (timeline entry)
/// </summary>
public class StatusPageIncidentUpdate : BaseEntity
{
    [Required]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Status at time of update: investigating, identified, monitoring, resolved
    /// </summary>
    [Required]
    [StringLength(50)]
    public string Status { get; set; } = string.Empty;

    public Guid StatusPageIncidentId { get; set; }
    public virtual StatusPageIncident Incident { get; set; } = null!;
}
