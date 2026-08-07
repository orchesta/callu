using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>Installation-wide organization settings — one row, pinned by a CHECK constraint on the primary key.</summary>
public class OrganizationSettings : BaseEntity
{
    /// <summary>Hard-coded primary key for the single settings row; any other Id is rejected by the CHECK constraint.</summary>
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Required]
    [StringLength(200)]
    public string OrganizationName { get; set; } = "Callu";

    public const int MaxDefaultTimezoneLength = 64;

    [Required]
    [StringLength(MaxDefaultTimezoneLength)]
    public string DefaultTimezone { get; set; } = "UTC";

    [Required]
    [StringLength(16)]
    public string DefaultCulture { get; set; } = "en-US";

    /// <summary>
    /// Public base URL used for links in outgoing emails (invitations, notifications, etc.).
    /// When empty the service layer falls back to the CalluSettings:ApiUrl configuration.
    /// </summary>
    public const int MaxBaseUrlLength = 500;

    [StringLength(MaxBaseUrlLength)]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Global kill-switch for outgoing email notifications.
    /// </summary>
    public bool EmailNotificationsEnabled { get; set; } = true;
}
