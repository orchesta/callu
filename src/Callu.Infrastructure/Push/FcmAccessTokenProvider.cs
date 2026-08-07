using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Callu.Infrastructure.Push;

public sealed class FcmAccessTokenProvider(IHttpClientFactory httpClientFactory, ILogger<FcmAccessTokenProvider> logger)
{
    private readonly object _gate = new();
    private string? _cachedToken;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAccessTokenAsync(string serviceAccountJson, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-2))
                return _cachedToken;
        }

        var token = await FetchAccessTokenAsync(serviceAccountJson, cancellationToken);
        lock (_gate)
        {
            _cachedToken = token.AccessToken;
            _expiresAt = token.ExpiresAt;
            return _cachedToken;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cachedToken = null;
            _expiresAt = default;
        }
    }

    public Task ProbeAsync(string serviceAccountJson, CancellationToken cancellationToken) =>
        FetchAccessTokenAsync(serviceAccountJson, cancellationToken);

    private async Task<(string AccessToken, DateTimeOffset ExpiresAt)> FetchAccessTokenAsync(
        string serviceAccountJson, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(serviceAccountJson);
        var root = doc.RootElement;
        var clientEmail = root.GetProperty("client_email").GetString()
            ?? throw new InvalidOperationException("client_email missing");
        var privateKeyPem = root.GetProperty("private_key").GetString()
            ?? throw new InvalidOperationException("private_key missing");
        var tokenUri = ResolveTokenUri(root);

        var jwt = CreateSignedJwt(clientEmail, privateKeyPem, tokenUri);
        var client = httpClientFactory.CreateClient("fcm");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = jwt
        });

        using var response = await client.PostAsync(tokenUri, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("FCM OAuth token exchange failed: {Status} {Body}", (int)response.StatusCode, Truncate(body));
            throw new InvalidOperationException($"FCM OAuth failed ({(int)response.StatusCode})");
        }

        using var tokenDoc = JsonDocument.Parse(body);
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("access_token missing");
        var expiresIn = tokenDoc.RootElement.TryGetProperty("expires_in", out var exp)
            ? exp.GetInt32()
            : 3600;

        return (accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    /// <summary>The OAuth endpoint the signed assertion may be sent to.</summary>
    // The assertion proves possession of the private key, so an arbitrary token_uri out of an
    // uploaded file would hand it to whoever wrote the file — and let the instance POST anywhere.
    private static string ResolveTokenUri(JsonElement root)
    {
        const string GoogleTokenUri = "https://oauth2.googleapis.com/token";

        if (!root.TryGetProperty("token_uri", out var tu) || tu.ValueKind != JsonValueKind.String)
            return GoogleTokenUri;

        var declared = tu.GetString();
        if (string.IsNullOrWhiteSpace(declared))
            return GoogleTokenUri;

        if (!Uri.TryCreate(declared, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !AllowedTokenHosts.Contains(uri.Host))
            throw new InvalidOperationException(
                "token_uri must be a Google OAuth endpoint (oauth2.googleapis.com or accounts.google.com)");

        return uri.ToString();
    }

    private static readonly HashSet<string> AllowedTokenHosts =
        new(StringComparer.OrdinalIgnoreCase) { "oauth2.googleapis.com", "accounts.google.com" };

    private static string CreateSignedJwt(string clientEmail, string privateKeyPem, string audience)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem.AsSpan());
        var key = new RsaSecurityKey(rsa);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: clientEmail,
            audience: audience,
            claims:
            [
                new System.Security.Claims.Claim("scope", "https://www.googleapis.com/auth/firebase.messaging"),
                new System.Security.Claims.Claim(JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            ],
            notBefore: now,
            expires: now.AddMinutes(55),
            signingCredentials: credentials);

        // Google expects iss = client_email; JwtSecurityToken issuer maps to iss.
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
