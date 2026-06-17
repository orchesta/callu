using Callu.Domain.Base;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Callu.Infrastructure.Identity;

/// <summary>
/// Application user extending ASP.NET Core Identity. Identity's <c>IdentityUser</c> base
/// stays (string Id, ConcurrencyStamp, etc.); audit + soft-delete are picked up via the
/// marker interfaces so the same interceptor and query filter that handle BaseEntity
/// rows also handle this user table. RowVersion is intentionally omitted — Identity
/// already ships ConcurrencyStamp for optimistic-concurrency.
/// </summary>
public class ApplicationUser : IdentityUser, IAuditableEntity, ISoftDeletable
{
    [StringLength(100)]
    public string? FirstName { get; set; }

    [StringLength(100)]
    public string? LastName { get; set; }

    /// <summary>Display name for the avatar dropdown.</summary>
    [StringLength(100)]
    public string? DisplayName { get; set; }

    /// <summary>Initials for the avatar circle.</summary>
    [StringLength(10)]
    public string? Initials { get; set; }

    [StringLength(500)]
    public string? AvatarUrl { get; set; }

    /// <summary>IANA timezone id, e.g. "Europe/Istanbul".</summary>
    [StringLength(100)]
    public string Timezone { get; set; } = "UTC";

    /// <summary>Locale for date/number formatting (e.g. "tr-TR", "en-US").</summary>
    [StringLength(20)]
    public string Culture { get; set; } = "en-US";

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
