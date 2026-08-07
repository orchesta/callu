using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Push;

public sealed class FirebaseCredentialProtector
{
    private const string Purpose = "Callu.FirebaseSettings.ServiceAccount.v1";
    private readonly IDataProtector _protector;
    private readonly ILogger<FirebaseCredentialProtector> _logger;

    public FirebaseCredentialProtector(IDataProtectionProvider provider, ILogger<FirebaseCredentialProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        return _protector.Protect(plaintext);
    }

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return null;
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex,
                "Firebase service account could not be decrypted — keyring may have rotated. Re-save credentials.");
            return null;
        }
    }
}
