using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents a team membership (many-to-many between Team and User)
/// </summary>
public class TeamMember : BaseEntity
{
    /// <summary>
    /// Team ID
    /// </summary>
    public Guid TeamId { get; set; }

    /// <summary>
    /// Navigation property for team
    /// </summary>
    public virtual Team Team { get; set; } = null!;

    /// <summary>
    /// User ID (references Identity user)
    /// </summary>
    [Required]
    [StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Member role in the team
    /// </summary>
    [StringLength(50)]
    public string? Role { get; set; }
}
