using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>Email subscriber for status page updates, double opt-in, with both tokens stored only as hashes.</summary>
public class StatusPageSubscriber : BaseEntity
{
    public Guid StatusPageId { get; set; }
    public virtual StatusPage StatusPage { get; set; } = null!;

    [Required]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>True only after the recipient clicks the confirmation link.</summary>
    public bool IsConfirmed { get; set; } = false;

    /// <summary>SHA-256 of the plaintext confirmation token mailed to the subscriber.</summary>
    [StringLength(128)]
    public string? ConfirmationTokenHash { get; set; }

    /// <summary>UTC deadline beyond which the confirmation token is rejected (default 24h after subscribe).</summary>
    public DateTime? ConfirmationTokenExpiresAt { get; set; }

    /// <summary>
    /// SHA-256 of the plaintext unsubscribe token. Issued once at subscribe time and
    /// embedded in every notification email's List-Unsubscribe header / footer link.
    /// </summary>
    [StringLength(128)]
    public string? UnsubscribeTokenHash { get; set; }

    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UnsubscribedAt { get; set; }
}
