using Callu.Infrastructure.Utilities;

namespace Callu.Tests;

/// <summary>Locks the SSRF guard's block and allow decisions, using literal IPs so no DNS is involved.</summary>
public class UrlSanitizerTests
{
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.5/internal")]
    [InlineData("http://192.168.1.10/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://100.100.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://2130706433/")]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///etc/passwd")]
    public void Blocks_Internal_Encoded_And_NonHttp(string url)
    {
        Assert.False(UrlSanitizer.IsValidHealthCheckUrl(url));
    }

    [Theory]
    [InlineData("http://93.184.216.34/")]
    [InlineData("https://1.1.1.1/webhook")]
    public void Allows_Public_Hosts(string url)
    {
        Assert.True(UrlSanitizer.IsValidHealthCheckUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    public void Blocks_Empty_Or_Malformed(string? url)
    {
        Assert.False(UrlSanitizer.IsValidHealthCheckUrl(url));
    }
}
