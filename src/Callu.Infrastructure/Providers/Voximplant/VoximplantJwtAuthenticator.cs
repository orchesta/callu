using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>Generates and caches short-lived JWTs for Voximplant Management API Bearer auth.</summary>
public sealed class VoximplantJwtAuthenticator : IDisposable
{
    private readonly long _accountId;
    private readonly string _keyId;
    private readonly RSA _rsa;

    private readonly object _lock = new();
    private string? _cachedToken;
    private DateTime _cachedUntilUtc = DateTime.MinValue;

    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(55);
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    public VoximplantJwtAuthenticator(string serviceAccountJson)
    {
        if (string.IsNullOrWhiteSpace(serviceAccountJson))
            throw new ArgumentException("Service account JSON is empty", nameof(serviceAccountJson));

        using var doc = JsonDocument.Parse(serviceAccountJson);
        var root = doc.RootElement;

        _accountId = root.GetProperty("account_id").GetInt64();
        _keyId = root.GetProperty("key_id").GetString()
            ?? throw new InvalidOperationException("service account JSON missing 'key_id'");
        var privateKeyPem = root.GetProperty("private_key").GetString()
            ?? throw new InvalidOperationException("service account JSON missing 'private_key'");

        _rsa = RSA.Create();
        _rsa.ImportFromPem(privateKeyPem);
    }

    public string GetBearerToken()
    {
        lock (_lock)
        {
            if (_cachedToken is not null && DateTime.UtcNow < _cachedUntilUtc - RefreshSkew)
                return _cachedToken;

            var now = DateTime.UtcNow;
            var credentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = _keyId }, SecurityAlgorithms.RsaSha256);

            var token = new JwtSecurityToken(
                issuer: _accountId.ToString(),
                audience: "voximplant.com",
                claims: new[]
                {
                    new Claim(JwtRegisteredClaimNames.Iss, _accountId.ToString()),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
                },
                notBefore: now,
                expires: now.Add(TokenTtl),
                signingCredentials: credentials);

            _cachedToken = new JwtSecurityTokenHandler().WriteToken(token);
            _cachedUntilUtc = now.Add(TokenTtl);
            return _cachedToken;
        }
    }

    public void Dispose() => _rsa.Dispose();
}
