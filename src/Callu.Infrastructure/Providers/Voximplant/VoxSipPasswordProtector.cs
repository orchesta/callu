using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>Encrypts SIP passwords at rest inside the <c>CallTokens.CallDataJson</c> blob; both hosts
/// must share one DataProtection keyring or neither can read what the other wrote.</summary>
public sealed class VoxSipPasswordProtector
{
    private const string Purpose = "Callu.Voximplant.CallToken.SipPassword.v1";

    public const string CipherPrefix = "enc:v1:";

    private readonly IDataProtector _protector;
    private readonly ILogger<VoxSipPasswordProtector> _logger;

    public VoxSipPasswordProtector(IDataProtectionProvider provider, ILogger<VoxSipPasswordProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        return CipherPrefix + _protector.Protect(plaintext);
    }

    /// <summary>Decrypts a prefixed SIP password, passing unprefixed ones through and returning empty on
    /// failure, which the scenario already handles.</summary>
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
            _logger.LogWarning(ex, "SIP password decryption failed — keyring may have rotated or payload tampered with.");
            return string.Empty;
        }
    }
}
