using System.Security.Cryptography;
using System.Text;
using Callu.Application.Services;

namespace Callu.Infrastructure.Services;

/// <summary>
/// HMAC-SHA256 webhook signature verifier.
/// Supports common formats: "sha256=hex", raw hex, and base64.
/// Uses constant-time comparison to prevent timing attacks.
/// </summary>
public class HmacWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    private const string Sha256Prefix = "sha256=";
    
    public bool Verify(string body, string secret, IDictionary<string, string> headers, string signatureHeaderName)
    {
        if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(secret))
            return false;
        
        if (!headers.TryGetValue(signatureHeaderName, out var signature) || string.IsNullOrEmpty(signature))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expectedHex = Convert.ToHexString(hash).ToLowerInvariant();

        if (signature.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var providedHex = signature[Sha256Prefix.Length..].ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedHex),
                Encoding.UTF8.GetBytes(providedHex));
        }

        var normalizedSignature = signature.ToLowerInvariant();
        if (normalizedSignature.Length == expectedHex.Length)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedHex),
                Encoding.UTF8.GetBytes(normalizedSignature));
        }

        try
        {
            var expectedBase64 = Convert.ToBase64String(hash);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedBase64),
                Encoding.UTF8.GetBytes(signature));
        }
        catch
        {
            return false;
        }
    }
}
