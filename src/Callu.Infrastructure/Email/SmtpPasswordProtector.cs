using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Email;

/// <summary>
/// Wraps ASP.NET Data Protection with a dedicated purpose string for SMTP passwords.
/// Both the API and Worker hosts must register <c>IDataProtectionProvider</c> against
/// the SAME keyring (filesystem volume in docker-compose, EF-store in clustered
/// setups) — otherwise the Worker can't read what the API just wrote. Losing the
/// keyring renders existing stored passwords unreadable; the operator must re-enter
/// SMTP credentials in the settings UI.
/// </summary>
public sealed class SmtpPasswordProtector
{
    private const string Purpose = "Callu.SmtpSettings.Password.v1";
    private readonly IDataProtector _protector;
    private readonly ILogger<SmtpPasswordProtector> _logger;

    public SmtpPasswordProtector(IDataProtectionProvider provider, ILogger<SmtpPasswordProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        return _protector.Protect(plaintext);
    }

    /// <summary>
    /// Returns the decrypted password, or null when the stored payload cannot be
    /// decrypted (keyring rotated/lost, payload corrupted). Callers must surface a
    /// "re-enter SMTP password" prompt rather than treating null as "no password".
    /// </summary>
    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return null;
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "SMTP password could not be decrypted — keyring may have rotated. Operator must re-save credentials.");
            return null;
        }
    }
}
