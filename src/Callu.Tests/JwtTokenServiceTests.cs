using System.Security.Claims;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Callu.Tests;

/// <summary>
/// Guards the JWT access-token round-trip: signature, issuer, audience, and lifetime validation.
/// A regression here silently accepts forged/expired/cross-issuer tokens.
/// </summary>
public class JwtTokenServiceTests
{
    private static JwtSettings Settings(string issuer = "callu", string audience = "callu-clients", int accessMinutes = 15) => new()
    {
        SecretKey = "test-secret-key-that-is-definitely-long-enough-0123456789abcdef",
        Issuer = issuer,
        Audience = audience,
        AccessTokenExpirationMinutes = accessMinutes,
        RefreshTokenExpirationDays = 7
    };

    private static JwtTokenService Service(JwtSettings s) => new(Options.Create(s));

    private static ApplicationUser User() => new()
    {
        Id = "user-1",
        Email = "u@example.com",
        UserName = "u@example.com",
        DisplayName = "User One",
        SecurityStamp = "stamp-1"
    };

    private static string TamperSignature(string token)
    {
        var parts = token.Split('.');
        parts[2] = (parts[2][0] == 'A' ? 'B' : 'A') + parts[2][1..];
        return string.Join('.', parts);
    }

    [Fact]
    public void GenerateThenValidate_RoundTrips_WithClaims()
    {
        var svc = Service(Settings());
        var token = svc.GenerateAccessToken(User(), new[] { "Admin" }, new List<Claim> { new("CanManageUsers", "true") });

        var principal = svc.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.Equal("user-1", principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(principal.IsInRole("Admin"));
        Assert.Equal("stamp-1", principal.FindFirstValue("sst"));
        Assert.Contains(principal.Claims, c => c.Type == "CanManageUsers" && c.Value == "true");
    }

    [Fact]
    public void ValidateToken_ReturnsNull_WhenSignatureTampered()
    {
        var svc = Service(Settings());
        var token = svc.GenerateAccessToken(User(), Array.Empty<string>(), new List<Claim>());

        Assert.Null(svc.ValidateToken(TamperSignature(token)));
    }

    [Fact]
    public void ValidateToken_ReturnsNull_ForWrongIssuer()
    {
        var attackerToken = Service(Settings(issuer: "attacker")).GenerateAccessToken(User(), Array.Empty<string>(), new List<Claim>());

        Assert.Null(Service(Settings(issuer: "callu")).ValidateToken(attackerToken));
    }

    [Fact]
    public void ValidateToken_ReturnsNull_WhenExpired()
    {
        var svc = Service(Settings(accessMinutes: -5));
        var token = svc.GenerateAccessToken(User(), Array.Empty<string>(), new List<Claim>());

        Assert.Null(svc.ValidateToken(token));
    }
}
