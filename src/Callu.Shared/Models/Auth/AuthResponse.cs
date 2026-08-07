namespace Callu.Shared.Models.Auth;

/// <summary>
/// Response model for authentication operations
/// </summary>
public class AuthResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Token { get; set; }
    
    /// <summary>
    /// When the access token expires (UTC)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
    
    public UserInfo? User { get; set; }
    
    /// <summary>
    /// Refresh token (opaque, for token rotation)
    /// </summary>
    public string? RefreshToken { get; set; }
}