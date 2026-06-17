using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Email subscriber for status page update notifications. Subscription requires
/// double opt-in: <see cref="IsConfirmed"/> stays false until the recipient clicks
/// the confirmation link sent to <see cref="Email"/>. Tokens are stored as SHA-256
/// hashes so a DB leak doesn't grant unsubscribe/confirm power.
/// </summary>
public class StatusPageSubscriber : BaseEntity
{
    public Guid StatusPageId { get; set; }
    public virtual StatusPage StatusPage { get; set; } = null!;

    [Required]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>True only after the recipient clicks the confirmation link.</summary>
    public bool IsConfirmed { get; set; } = false;

    /// <summary>
    /// SHA-256 (64 hex chars) of the plaintext confirmation token mailed to the
    /// subscriber. Stored hashed so a DB leak alone does not let an attacker confirm
    /// arbitrary subscriptions.
    /// </summary>
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
