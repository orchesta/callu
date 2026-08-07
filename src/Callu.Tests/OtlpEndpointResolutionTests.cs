using Callu.Infrastructure.Telemetry;

namespace Callu.Tests;

/// <summary>A schemeless OTLP endpoint must never take the host down at service-registration time.</summary>
public class OtlpEndpointResolutionTests
{
    [Theory]
    [InlineData("http://callu-jaeger:4317", "http://callu-jaeger:4317/")]
    [InlineData("https://collector.example.io:4318/", "https://collector.example.io:4318/")]
    public void AFullUrl_IsUsedAsGiven(string configured, string expected)
    {
        Assert.True(TelemetryExtensions.TryResolveOtlpEndpoint(configured, out var endpoint));
        Assert.Equal(expected, endpoint.ToString());
    }

    [Theory]
    [InlineData("otel-collector:4317")]
    [InlineData("collector.internal:4318")]
    [InlineData("192.168.1.10:4317")]
    public void HostAndPortWithNoScheme_AssumesHttp(string configured)
    {
        Assert.True(TelemetryExtensions.TryResolveOtlpEndpoint(configured, out var endpoint));

        Assert.Equal(Uri.UriSchemeHttp, endpoint.Scheme);
        Assert.Equal(configured.Split(':')[0], endpoint.Host);
        Assert.Equal(int.Parse(configured.Split(':')[1]), endpoint.Port);
    }

    [Fact]
    public void ABareHostname_AssumesHttp()
    {
        Assert.True(TelemetryExtensions.TryResolveOtlpEndpoint("otel-collector", out var endpoint));

        Assert.Equal("http://otel-collector/", endpoint.ToString());
    }

    [Theory]
    [InlineData("otel collector:4317")]
    [InlineData("://4317")]
    [InlineData("   ")]
    public void SomethingUnusable_DisablesExportInsteadOfThrowing(string configured)
    {
        Assert.False(TelemetryExtensions.TryResolveOtlpEndpoint(configured, out _));
    }

    [Fact]
    public void ANonHttpScheme_IsNotAcceptedAsAnEndpoint()
    {
        // Uri.TryCreate accepts "otel-collector:4317" as an absolute URI whose SCHEME is
        // "otel-collector" — the trap this resolver exists for.
        Assert.True(Uri.TryCreate("otel-collector:4317", UriKind.Absolute, out var parsed));
        Assert.Equal("otel-collector", parsed!.Scheme);
        Assert.Empty(parsed.Host);

        Assert.True(TelemetryExtensions.TryResolveOtlpEndpoint("otel-collector:4317", out var resolved));
        Assert.NotEqual(parsed.Scheme, resolved.Scheme);
    }
}
