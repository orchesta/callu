namespace Callu.Application.Services;

/// <summary>
/// Verifies HMAC signatures on webhook payloads
/// </summary>
public interface IWebhookSignatureVerifier
{
    /// <summary>
    /// Verify HMAC-SHA256 signature from webhook headers.
    /// </summary>
    /// <param name="body">Raw request body</param>
    /// <param name="secret">Webhook secret configured for the service</param>
    /// <param name="headers">Request headers</param>
    /// <param name="signatureHeaderName">Header containing the HMAC signature (e.g., "X-Hub-Signature-256")</param>
    /// <returns>True if signature is valid</returns>
    bool Verify(string body, string secret, IDictionary<string, string> headers, string signatureHeaderName);
}
