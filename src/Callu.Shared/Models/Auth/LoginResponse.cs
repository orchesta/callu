namespace Callu.Shared.Models.Auth;

/// <summary>
/// Response data returned on successful login / refresh.
/// </summary>
public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public UserInfo User { get; set; } = null!;

    /// <summary>
    /// Opaque refresh token for native clients (Keychain / Keystore). The SPA also receives it as
    /// an HttpOnly cookie and can ignore this field.
    /// </summary>
    public string? RefreshToken { get; set; }
}
