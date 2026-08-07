using System.Net;
using Callu.Infrastructure.Utilities;

namespace Callu.Tests;

/// <summary>
/// The private-network opt-out permits RFC1918 targets but keeps loopback, link-local, CGNAT, and
/// cloud-metadata addresses blocked in every mode.
/// </summary>
public class UrlSanitizerPrivateOptOutTests
{
    [Theory]
    [InlineData("169.254.169.254")] // cloud metadata / link-local
    [InlineData("127.0.0.1")]       // loopback
    [InlineData("100.64.0.1")]      // CGNAT
    [InlineData("0.0.0.0")]         // unspecified
    public void Metadata_and_loopback_are_always_blocked(string ip)
    {
        Assert.True(UrlSanitizer.IsMetadataIp(IPAddress.Parse(ip)));
        Assert.False(UrlSanitizer.IsAllowedTargetIp(IPAddress.Parse(ip), allowPrivate: true));
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.20")]
    [InlineData("172.16.4.4")]
    public void Private_ranges_allowed_only_with_opt_in(string ip)
    {
        var addr = IPAddress.Parse(ip);
        Assert.False(UrlSanitizer.IsMetadataIp(addr));
        Assert.True(UrlSanitizer.IsAllowedTargetIp(addr, allowPrivate: true));
        Assert.False(UrlSanitizer.IsAllowedTargetIp(addr, allowPrivate: false));
    }

    [Fact]
    public void Public_ip_allowed_in_both_modes()
    {
        var addr = IPAddress.Parse("8.8.8.8");
        Assert.True(UrlSanitizer.IsAllowedTargetIp(addr, allowPrivate: false));
        Assert.True(UrlSanitizer.IsAllowedTargetIp(addr, allowPrivate: true));
    }

    [Fact]
    public void Url_opt_in_accepts_private_literal_but_not_metadata()
    {
        // Literal IPs so the DNS pass in IsValidHealthCheckUrl short-circuits (hermetic offline).
        Assert.True(UrlSanitizer.IsValidHealthCheckUrl("http://10.0.0.5/probe", allowPrivate: true));
        Assert.False(UrlSanitizer.IsValidHealthCheckUrl("http://10.0.0.5/probe", allowPrivate: false));
        Assert.False(UrlSanitizer.IsValidHealthCheckUrl("http://169.254.169.254/latest/meta-data", allowPrivate: true));
    }
}
