using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Database-managed email template for user customization.
/// Separate from Infrastructure/Email/EmailTemplates.cs which is a static file-based loader.
/// </summary>
public class EmailTemplate : BaseEntity
{
    /// <summary>
    /// Display name
    /// </summary>
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Unique slug key (e.g. "connection-test", "invitation", "password-reset")
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Email subject line (supports {{variable}} placeholders)
    /// </summary>
    [Required]
    [StringLength(500)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Full HTML body content
    /// </summary>
    [Required]
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>
    /// Optional plain text fallback body
    /// </summary>
    public string? PlainTextBody { get; set; }

    /// <summary>
    /// Template description
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// System templates cannot be deleted
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Active flag — inactive templates are not used for sending
    /// </summary>
    public bool IsActive { get; set; } = true;
}
