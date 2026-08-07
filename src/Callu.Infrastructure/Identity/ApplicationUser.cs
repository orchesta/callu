using Callu.Domain.Base;
using Callu.Shared.Validation;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Callu.Infrastructure.Identity;

/// <summary>Application user extending ASP.NET Core Identity, with audit and soft-delete markers.</summary>
public class ApplicationUser : IdentityUser, IAuditableEntity, ISoftDeletable
{
    [StringLength(IdentityFieldLengths.FirstName)]
    public string? FirstName { get; set; }

    [StringLength(IdentityFieldLengths.LastName)]
    public string? LastName { get; set; }

    /// <summary>Display name for the avatar dropdown.</summary>
    [StringLength(IdentityFieldLengths.DisplayName)]
    public string? DisplayName { get; set; }

    /// <summary>Initials for the avatar circle.</summary>
    [StringLength(10)]
    public string? Initials { get; set; }

    [StringLength(500)]
    public string? AvatarUrl { get; set; }

    /// <summary>IANA timezone id, e.g. "Europe/Istanbul".</summary>
    [StringLength(IdentityFieldLengths.Timezone)]
    public string Timezone { get; set; } = "UTC";

    /// <summary>The language this person reads and is spoken to in; null means they have not chosen.</summary>
    [StringLength(20)]
    public string? Culture { get; set; }

    /// <summary>Presence string surfaced in the user list (Offline / Online / Busy / …).</summary>
    [StringLength(50)]
    public string Status { get; set; } = "Offline";

    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
}
