using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>
/// Encrypts SIP passwords at rest inside the <c>CallTokens.CallDataJson</c> blob.
/// Wire payload to VoxEngine stays unchanged — controller decrypts before serialising
/// the response to the scenario. Both API and Worker hosts MUST share the same
/// DataProtection keyring or the API can't read what the Worker's retry job wrote
/// (and vice versa).
/// </summary>
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

    /// <summary>
    /// Decrypts a SIP password stored with the <see cref="CipherPrefix"/> sentinel.
    /// Values without the prefix are returned as-is (rollback compatibility). Returns
    /// empty string when decryption fails — the VoxEngine scenario already handles
    /// missing credentials by calling <c>callSIP</c> with <c>password: ""</c>.
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
            _logger.LogWarning(ex, "SIP password decryption failed — keyring may have rotated or payload tampered with.");
            return string.Empty;
        }
    }
}
