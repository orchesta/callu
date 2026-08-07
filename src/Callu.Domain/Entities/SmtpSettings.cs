using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// SMTP configuration settings for email delivery
/// </summary>
public class SmtpSettings : BaseEntity
{
    /// <summary>
    /// Fixed primary key for the single settings row, so a concurrent second insert loses on the PK.
    /// </summary>
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    /// <summary>
    /// SMTP server hostname (e.g., smtp.gmail.com)
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// SMTP server port (typically 587 for TLS, 465 for SSL, 25 for unencrypted)
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Enable SSL/TLS encryption
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// SMTP authentication username
    /// </summary>
    [StringLength(255)]
    public string? Username { get; set; }

    /// <summary>SMTP authentication password, always stored as an encrypted payload — never plaintext.</summary>
    [StringLength(2000)]
    public string? Password { get; set; }

    /// <summary>
    /// Sender email address
    /// </summary>
    [Required]
    [StringLength(255)]
    [EmailAddress]
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Sender display name
    /// </summary>
    [StringLength(100)]
    public string FromName { get; set; } = "CalluApp";

    /// <summary>Optional Reply-To address, so replies can route somewhere other than the no-reply sender.</summary>
    [StringLength(255)]
    [EmailAddress]
    public string? ReplyToAddress { get; set; }

    /// <summary>
    /// Whether SMTP is properly configured and tested
    /// </summary>
    public bool IsConfigured { get; set; } = false;

    /// <summary>
    /// Last successful connection test timestamp
    /// </summary>
    public DateTime? LastTestedAt { get; set; }

    /// <summary>
    /// Last test result message
    /// </summary>
    [StringLength(500)]
    public string? LastTestResult { get; set; }
}
