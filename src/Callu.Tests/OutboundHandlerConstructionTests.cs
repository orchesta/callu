using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.DI;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Tests;

/// <summary>
/// Webhook dispatch, status-page health checks and HTTP SMS all hang off one pinned handler. If
/// building it throws, none of them ever reach the network — and the operator sees a bare 400.
/// </summary>
public class OutboundHandlerConstructionTests
{
    private static ServiceProvider Build(bool allowPrivate = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<CommunicationSettingsOptions>(o =>
        {
            o.AllowPrivateWebhookEndpoint = allowPrivate;
            o.AllowPrivateHealthCheckEndpoint = allowPrivate;
            o.AllowPrivateSmsEndpoint = allowPrivate;
        });
        services.AddCommunicationModule(disableSsl: false);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creating the client is what builds the primary handler, so this is the request path's first
    /// step — not a proxy for it.
    /// </summary>
    [Theory]
    [InlineData("WebhookDispatch")]
    [InlineData("HealthCheck")]
    [InlineData("HttpSms")]
    [InlineData("Voximplant")]
    [InlineData("Verimor")]
    public void EveryOutboundClient_CanBeBuilt(string clientName)
    {
        using var provider = Build();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        Assert.NotNull(client);
    }

    /// <summary>The private-endpoint opt-ins choose a different handler, so they are built too.</summary>
    [Theory]
    [InlineData("WebhookDispatch")]
    [InlineData("HealthCheck")]
    [InlineData("HttpSms")]
    public void EveryOutboundClient_CanBeBuilt_WithPrivateEndpointsAllowed(string clientName)
    {
        using var provider = Build(allowPrivate: true);

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        Assert.NotNull(client);
    }
}
