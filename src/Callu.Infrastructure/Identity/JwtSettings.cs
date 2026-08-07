namespace Callu.Infrastructure.Identity;

/// <summary>
/// JWT token configuration settings
/// </summary>
public class JwtSettings
{
    public const string SectionName = "JwtSettings";
    
    /// <summary>
    /// Secret key for signing tokens (min 32 characters)
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Token issuer
    /// </summary>
    public string Issuer { get; set; } = "CalluApp";
    
    /// <summary>
    /// Token audience
    /// </summary>
    public string Audience { get; set; } = "CalluApp";
    
    /// <summary>
    /// Access token expiration in minutes (default: 15)
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    
    /// <summary>
    /// Refresh token expiration in days (default: 7)
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
