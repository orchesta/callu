using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>
/// Encrypts the long-lived SIP trunk password at rest (<c>SipTrunkSettings.Password</c>).
/// This is the credential that authorizes outbound PSTN calls, so it carries direct
/// toll-fraud risk if leaked from a DB backup/replica. Distinct purpose from
/// <see cref="VoxSipPasswordProtector"/> (which protects the per-call copy inside the
/// CallToken). Both API and Worker hosts MUST share the same DataProtection keyring.
/// </summary>
public sealed class SipTrunkPasswordProtector
{
    private const string Purpose = "Callu.SipTrunk.Password.v1";

    /// <summary>Sentinel marking an encrypted value; lets the read path pass plaintext through unchanged.</summary>
    public const string CipherPrefix = "enc:v1:";

    private readonly IDataProtector _protector;
    private readonly ILogger<SipTrunkPasswordProtector> _logger;

    public SipTrunkPasswordProtector(IDataProtectionProvider provider, ILogger<SipTrunkPasswordProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        if (plaintext.StartsWith(CipherPrefix, StringComparison.Ordinal)) return plaintext;
        return CipherPrefix + _protector.Protect(plaintext);
    }

    public string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (!stored.StartsWith(CipherPrefix, StringComparison.Ordinal))
            return stored;

        try
        {
            return _protector.Unprotect(stored[CipherPrefix.Length..]);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "SIP trunk password decryption failed — keyring may have rotated or payload tampered with.");
            return string.Empty;
        }
    }
}
