using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers;

/// <summary>Encrypts individual secret values inside <c>CommunicationProvider.ConfigJson</c>, leaving the
/// surrounding JSON shape intact; the API and Worker must share one DataProtection keyring.</summary>
public sealed class ProviderSecretProtector
{
    private const string Purpose = "Callu.CommunicationProvider.ConfigSecret.v1";

    /// <summary>Sentinel marking an encrypted value; lets the read path pass plaintext through unchanged.</summary>
    public const string CipherPrefix = "enc:v1:";

    private readonly IDataProtector _protector;
    private readonly ILogger<ProviderSecretProtector> _logger;

    public ProviderSecretProtector(IDataProtectionProvider provider, ILogger<ProviderSecretProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    /// <summary>
    /// Encrypts a plaintext secret. Empty/null → empty. Already-encrypted values (prefixed) are
    /// returned unchanged, so the helper is idempotent across the read-merge-write config path.
    /// </summary>
    public string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        if (plaintext.StartsWith(CipherPrefix, StringComparison.Ordinal)) return plaintext;
        return CipherPrefix + _protector.Protect(plaintext);
    }

    /// <summary>Decrypts a prefixed value, passing unprefixed ones through and returning empty on a
    /// decryption failure so callers fail closed rather than throw.</summary>
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
            _logger.LogWarning(ex, "Provider config secret decryption failed — keyring may have rotated or payload tampered with.");
            return string.Empty;
        }
    }
}
