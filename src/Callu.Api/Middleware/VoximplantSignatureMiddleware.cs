using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Callu.Api.Middleware;

/// <summary>
/// Optional HMAC-SHA256 verification of Voximplant callback bodies.
///
/// When <see cref="VoximplantSignatureOptions.RequireSignature"/> is enabled, requests to
/// <c>/api/voximplant/*</c> must include an <c>X-Signature</c> header whose value is the
/// lowercase hex-encoded HMAC-SHA256 of <c>"{timestamp}.{nonce}.{rawBody}"</c> using the
/// shared secret configured in <see cref="VoximplantSignatureOptions.Secret"/>.
///
/// This is an additional layer on top of the existing scenario key + timestamp + nonce
/// checks: even if a scenario key leaks, an attacker still needs the signing secret to
/// forge a payload (e.g. fake an "Acknowledged" callback that silences escalation).
///
/// Flag defaults to off so existing deployments keep working during the rollout; enable
/// after updating the VoxEngine scenario scripts to sign their outgoing requests.
/// </summary>
public sealed class VoximplantSignatureMiddleware(
    RequestDelegate next,
    IOptionsMonitor<VoximplantSignatureOptions> optionsMonitor,
    ILogger<VoximplantSignatureMiddleware> logger)
{
    private static readonly PathString[] ProtectedPaths =
    [
        new("/api/voximplant/call-data"),
        new("/api/voximplant/callback"),
        new("/api/voximplant/conference-room"),
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var options = optionsMonitor.CurrentValue;

        if (!options.RequireSignature || !IsProtectedPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Secret))
        {
            logger.LogError("Voximplant signature verification enabled but secret is empty. Rejecting request.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        var signature = context.Request.Headers["X-Signature"].ToString();
        var timestamp = context.Request.Headers["X-Timestamp"].ToString();
        var nonce = context.Request.Headers["X-Nonce"].ToString();

        if (string.IsNullOrWhiteSpace(signature) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(nonce))
        {
            logger.LogWarning("Voximplant callback rejected: missing signature/timestamp/nonce headers");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(
                   context.Request.Body,
                   encoding: Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: false,
                   bufferSize: 8192,
                   leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(context.RequestAborted);
        }
        context.Request.Body.Position = 0;

        var expected = ComputeHmacHex(options.Secret!, $"{timestamp}.{nonce}.{rawBody}");

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(signature.ToLowerInvariant())))
        {
            logger.LogWarning("Voximplant callback rejected: HMAC mismatch for {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    private static string ComputeHmacHex(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsProtectedPath(PathString path)
    {
        foreach (var prefix in ProtectedPaths)
        {
            if (path.StartsWithSegments(prefix)) return true;
        }
        return false;
    }
}

public sealed class VoximplantSignatureOptions
{
    public const string SectionName = "Voximplant:Signature";

    public bool RequireSignature { get; set; } = false;

    public string? Secret { get; set; }
}
