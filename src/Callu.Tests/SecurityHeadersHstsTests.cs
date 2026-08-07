using Callu.Api.Middleware;
using Microsoft.Extensions.Configuration;

namespace Callu.Tests;

public class SecurityHeadersHstsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    /// <summary>An install that never configured HSTS must still be recoverable within minutes.</summary>
    [Fact]
    public void Default_IsShortLived_AndNeitherPreloadNorSubdomains()
    {
        var value = SecurityHeadersMiddleware.BuildHstsValue(Config());

        Assert.Equal($"max-age={SecurityHeadersMiddleware.DefaultHstsMaxAgeSeconds}", value);
        Assert.DoesNotContain("preload", value, StringComparison.Ordinal);
        Assert.DoesNotContain("includeSubDomains", value, StringComparison.Ordinal);
        Assert.True(SecurityHeadersMiddleware.DefaultHstsMaxAgeSeconds <= 3600);
    }

    [Fact]
    public void OperatorCanOptIn_ToTheStrongPolicy()
    {
        var value = SecurityHeadersMiddleware.BuildHstsValue(Config(
            ("Callu:Security:HstsMaxAgeSeconds", "31536000"),
            ("Callu:Security:HstsIncludeSubDomains", "true"),
            ("Callu:Security:HstsPreload", "true")));

        Assert.Equal("max-age=31536000; includeSubDomains; preload", value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonPositiveMaxAge_OmitsTheHeader(string maxAge)
    {
        Assert.Null(SecurityHeadersMiddleware.BuildHstsValue(
            Config(("Callu:Security:HstsMaxAgeSeconds", maxAge))));
    }
}
