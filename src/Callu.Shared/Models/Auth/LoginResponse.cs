namespace Callu.Shared.Models.Auth;

/// <summary>
/// Response data returned on successful login
/// </summary>
public class LoginResponse
{
    /// <summary>
    /// JWT access token
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;
    
    /// <summary>
    /// When the access token expires (UTC)
    /// </summary>
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>
    /// Authenticated user info
    /// </summary>
    public UserInfo User { get; set; } = null!;
}
