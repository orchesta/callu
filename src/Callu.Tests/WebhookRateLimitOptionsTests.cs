using Callu.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Callu.Tests;

/// <summary>The webhook ingest limits are configurable, and their defaults survive an empty config.</summary>
public class WebhookRateLimitOptionsTests
{
    [Fact]
    public void Defaults_AreAStormTolerantThousandPerMinute()
    {
        var options = new WebhookRateLimitOptions();

        Assert.Equal(1000, options.PermitLimit);
        Assert.Equal(60, options.WindowSeconds);
        Assert.Equal(10, options.QueueLimit);
    }

    [Fact]
    public void TheSection_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Callu:WebhookRateLimit:PermitLimit"] = "250",
                ["Callu:WebhookRateLimit:WindowSeconds"] = "30",
                ["Callu:WebhookRateLimit:QueueLimit"] = "5",
            })
            .Build();

        var options = config.GetSection(WebhookRateLimitOptions.SectionName).Get<WebhookRateLimitOptions>();

        Assert.NotNull(options);
        Assert.Equal(250, options!.PermitLimit);
        Assert.Equal(30, options.WindowSeconds);
        Assert.Equal(5, options.QueueLimit);
    }

    [Fact]
    public void AMissingSection_FallsBackToDefaults()
    {
        var config = new ConfigurationBuilder().Build();

        var options = config.GetSection(WebhookRateLimitOptions.SectionName).Get<WebhookRateLimitOptions>()
                      ?? new WebhookRateLimitOptions();

        Assert.Equal(1000, options.PermitLimit);
    }
}
