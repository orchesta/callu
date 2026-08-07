using Callu.Infrastructure.Services;

namespace Callu.Tests;

/// <summary>
/// SEC-3: listening-mode webhook captures must not persist secret/identity header values
/// (they are returned to admins verbatim by CapturesController), and the body is size-bounded.
/// </summary>
public class WebhookCaptureRedactionTests
{
    [Fact]
    public void Redacts_Sensitive_Headers_Preserves_Benign()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer super-secret",
            ["Cookie"] = "session=abc123",
            ["X-Api-Key"] = "key-123",
            ["X-Hub-Signature-256"] = "sha256=deadbeef",
            ["X-My-Sig"] = "provider-sig",
            ["Content-Type"] = "application/json",
            ["User-Agent"] = "Prometheus/2.0",
        };

        var safe = WebhookProcessingService.RedactSensitiveHeaders(headers, "X-My-Sig");

        Assert.Equal("***redacted***", safe["Authorization"]);
        Assert.Equal("***redacted***", safe["Cookie"]);
        Assert.Equal("***redacted***", safe["X-Api-Key"]);
        Assert.Equal("***redacted***", safe["X-Hub-Signature-256"]);
        Assert.Equal("***redacted***", safe["X-My-Sig"]);
        Assert.Equal("application/json", safe["Content-Type"]);
        Assert.Equal("Prometheus/2.0", safe["User-Agent"]);
    }

    /// <summary>The ingest key travels in a header, which puts it in the captured request.</summary>
    [Theory]
    [InlineData("X-Callu-Api-Key")]
    [InlineData("x-callu-api-key")]
    [InlineData("X-Api-Key")]
    public void Redacts_Every_Accepted_Ingest_Key_Header(string header)
    {
        var headers = new Dictionary<string, string> { [header] = "key-123" };

        var safe = WebhookProcessingService.RedactSensitiveHeaders(headers, null);

        Assert.Equal("***redacted***", safe[header]);
    }

    [Fact]
    public void Trims_Oversized_Body()
    {
        var big = new string('x', 70 * 1024);
        var trimmed = WebhookProcessingService.TrimForCapture(big);

        Assert.True(trimmed.Length < big.Length);
        Assert.EndsWith("...[truncated]", trimmed);
    }

    [Fact]
    public void Keeps_Small_Body_Intact()
    {
        const string body = "{\"alert\":\"db down\"}";
        Assert.Equal(body, WebhookProcessingService.TrimForCapture(body));
    }
}
