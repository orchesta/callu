using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Persisted one-time call token for VoxEngine data fetch.
/// Survives app restarts, auto-cleaned after expiry.
/// </summary>
public class CallToken : BaseEntity
{
    /// <summary>
    /// Unique token string (GUID without hyphens)
    /// </summary>
    [Required]
    [StringLength(128)]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Serialized VoxCallData as JSON
    /// </summary>
    [Required]
    public string CallDataJson { get; set; } = string.Empty;

    /// <summary>
    /// When this token expires (UTC)
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether the token has been consumed (one-time use).
    /// Kept for Expand phase; <see cref="ConsumedAt"/> is the source of truth going forward.
    /// </summary>
    public bool IsConsumed { get; set; }

    /// <summary>
    /// UTC timestamp of consumption. Null = token still usable.
    /// </summary>
    public DateTime? ConsumedAt { get; set; }
}
