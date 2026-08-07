using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Callu.Infrastructure.Identity;

/// <summary>
/// Application role extending ASP.NET Core Identity
/// </summary>
public class ApplicationRole : IdentityRole
{
    public ApplicationRole() : base() { }
    
    public ApplicationRole(string roleName) : base(roleName) 
    {
        CreatedAt = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Role description
    /// </summary>
    [StringLength(1024)]
    public string? Description { get; set; }

    /// <summary>
    /// When the role was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
