using Callu.Infrastructure.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>
/// SEC-1: ProviderSecretProtector encrypts provider-config secrets at rest with a sentinel
/// prefix, is idempotent, and tolerates plaintext (rollback) values.
/// </summary>
public class ProviderSecretProtectorTests
{
    private static ProviderSecretProtector New() =>
        new(new EphemeralDataProtectionProvider(), NullLogger<ProviderSecretProtector>.Instance);

    [Fact]
    public void RoundTrips_A_Secret()
    {
        var p = New();
        var enc = p.Protect("super-secret-api-key");

        Assert.StartsWith("enc:v1:", enc);
        Assert.NotEqual("super-secret-api-key", enc);
        Assert.Equal("super-secret-api-key", p.Unprotect(enc));
    }

    [Fact]
    public void Protect_Is_Idempotent_On_Already_Encrypted()
    {
        var p = New();
        var enc = p.Protect("k");

        Assert.Equal(enc, p.Protect(enc));
    }

    [Fact]
    public void Unprotect_Passes_Through_Plaintext()
    {
        var p = New();
        Assert.Equal("plain", p.Unprotect("plain"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_Inputs_Yield_Empty(string? input)
    {
        var p = New();
        Assert.Equal("", p.Protect(input));
        Assert.Equal("", p.Unprotect(input));
    }
}
