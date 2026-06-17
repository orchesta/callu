namespace Callu.Domain.Entities;

/// <summary>
/// Refresh token for JWT token rotation.
/// Token is stored as SHA256 hash — plaintext is never persisted.
/// Supports token family tracking for theft detection.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// The user this refresh token belongs to
    /// </summary>
    public string UserId { get; set; } = string.Empty;
    
    /// <summary>
    /// SHA256 hash of the refresh token (plaintext never stored)
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;
    
    /// <summary>
    /// When this token expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>
    /// When this token was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// When this token was revoked (null if still active)
    /// </summary>
    public DateTime? RevokedAt { get; set; }
    
    /// <summary>
    /// The token hash that replaced this one (for rotation tracking)
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }
    
    /// <summary>
    /// Token family identifier — all tokens in a rotation chain share the same family.
    /// If a revoked token is reused, the entire family is invalidated.
    /// </summary>
    public Guid FamilyId { get; set; }
    
    /// <summary>
    /// IP address of the client that created this token
    /// </summary>
    public string? CreatedByIp { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;
}
