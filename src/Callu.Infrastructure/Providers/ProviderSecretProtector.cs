using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers;

/// <summary>
/// Encrypts communication-provider secrets at rest inside <c>CommunicationProvider.ConfigJson</c>
/// — Voximplant api_key / service-account JSON / scenario API key, and HTTP-SMS credentials.
/// Field-level: only secret VALUES are wrapped (with the <see cref="CipherPrefix"/> sentinel),
/// the surrounding JSON shape is preserved so the many non-secret readers keep working.
/// Both API and Worker hosts MUST share the same DataProtection keyring. Mirrors
/// <see cref="Voximplant.VoxSipPasswordProtector"/>.
/// </summary>
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

    /// <summary>
    /// Decrypts a value stored with <see cref="CipherPrefix"/>. Values without the prefix are
    /// returned as-is (rollback / plaintext tolerance). Returns empty on a decryption failure
    /// (keyring rotation / tampering) so callers fail closed rather than throwing.
    /// </summary>
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
