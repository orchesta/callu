using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Installation-wide organization settings. Single row expected (Callu is single-tenant);
/// uniqueness is pinned at the DB layer by a CHECK constraint on the primary key matching
/// <see cref="SingletonId"/>, which makes a duplicate insert race fail loudly instead of
/// silently producing two rows.
/// </summary>
public class OrganizationSettings : BaseEntity
{
    /// <summary>
    /// Hard-coded primary key for the single settings row. Any insert with a
    /// different Id will be rejected by the CK_OrganizationSettings_Singleton
    /// CHECK constraint applied in the Initial migration.
    /// </summary>
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Required]
    [StringLength(200)]
    public string OrganizationName { get; set; } = "Callu";

    [Required]
    [StringLength(64)]
    public string DefaultTimezone { get; set; } = "UTC";

    [Required]
    [StringLength(16)]
    public string DefaultCulture { get; set; } = "en-US";

    /// <summary>
    /// Public base URL used for links in outgoing emails (invitations, notifications, etc.).
    /// When empty the service layer falls back to the CalluSettings:ApiUrl configuration.
    /// </summary>
    [StringLength(500)]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Global kill-switch for outgoing email notifications.
    /// </summary>
    public bool EmailNotificationsEnabled { get; set; } = true;
}
