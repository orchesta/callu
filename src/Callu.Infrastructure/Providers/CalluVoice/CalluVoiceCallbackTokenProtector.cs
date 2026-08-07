using System.Security.Cryptography;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;

namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>Mints and verifies the per-call token that authenticates a callu-voice status callback.</summary>
// callu-voice sends no bearer, no signature and no timestamp, and it retries the same body, so the
// secret has to travel in the callback URL itself and the receiver has to tolerate a repeat.
public sealed class CalluVoiceCallbackTokenProtector
{
    public const string Purpose = "Callu.CalluVoice.CallbackToken.v1";

    /// <summary>Covers the ring, the service's own call cap and its callback retry budget, with room to spare.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private const char Separator = '|';

    private readonly ITimeLimitedDataProtector _protector;

    public CalluVoiceCallbackTokenProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>Null when there is no incident or no call id to bind to, so no token is issued at all.</summary>
    public string? Issue(Guid incidentId, string? callId, string? phoneNumber)
    {
        if (incidentId == Guid.Empty || string.IsNullOrWhiteSpace(callId))
            return null;

        var sealedPayload = string.Concat(
            incidentId.ToString("N"), Separator, callId.Trim(), Separator, phoneNumber?.Trim() ?? string.Empty);

        return _protector.Protect(sealedPayload, Lifetime);
    }

    /// <summary>
    /// True only for a token this installation sealed, still inside its lifetime. Callers must not fall
    /// back to anything the request body claims when this is false.
    /// </summary>
    public bool TryResolve(string? token, out CalluVoiceCallbackTicket ticket)
    {
        ticket = null!;
        if (string.IsNullOrWhiteSpace(token)) return false;

        string payload;
        try
        {
            payload = _protector.Unprotect(token.Trim());
        }
        catch (CryptographicException)
        {
            // Tampered, expired, or sealed under a keyring this host cannot read.
            return false;
        }

        var parts = payload.Split(Separator, 3);
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var incidentId)
            || incidentId == Guid.Empty || parts[1].Length == 0)
            return false;

        ticket = new CalluVoiceCallbackTicket(incidentId, parts[1], parts[2]);
        return true;
    }

    /// <summary>The query parameter the token travels in.</summary>
    // The query and not a path segment: nginx's access log writes the path of every proxied API request
    // and skips the query, so a secret in the path is a secret on disk.
    public const string TokenQueryKey = "token";

    /// <summary>The address one call reports on: the configured address, the callback path, and this call's token.</summary>
    public static string CallbackUrlFor(string callbackUrl, string? token)
    {
        var builder = new UriBuilder(callbackUrl.Trim()) { Path = CalluVoiceConfig.CallbackPath };

        if (!string.IsNullOrEmpty(token))
            builder.Query = $"{TokenQueryKey}={Uri.EscapeDataString(token)}";

        return builder.Uri.ToString();
    }
}
