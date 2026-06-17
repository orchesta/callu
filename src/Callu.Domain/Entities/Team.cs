using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents a team in the organization
/// </summary>
public class Team : BaseEntity
{

    /// <summary>
    /// Team name
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Team description
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Team icon/emoji
    /// </summary>
    [StringLength(10)]
    public string? Icon { get; set; }

    /// <summary>
    /// Team color for UI
    /// </summary>
    [StringLength(20)]
    public string? Color { get; set; }

    /// <summary>
    /// Team members
    /// </summary>
    public virtual ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();

    /// <summary>
    /// Incidents assigned to this team
    /// </summary>
    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    /// <summary>
    /// Services owned by this team
    /// </summary>
    public virtual ICollection<Service> Services { get; set; } = new List<Service>();
}
