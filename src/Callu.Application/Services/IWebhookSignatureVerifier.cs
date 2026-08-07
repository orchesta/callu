namespace Callu.Application.Services;

/// <summary>
/// Verifies HMAC signatures on webhook payloads
/// </summary>
public interface IWebhookSignatureVerifier
{
    /// <summary>Verify an HMAC-SHA256 signature carried in the named header, e.g. X-Hub-Signature-256.</summary>
    bool Verify(string body, string secret, IDictionary<string, string> headers, string signatureHeaderName);
}
