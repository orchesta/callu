using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Messaging.Broker;
using Callu.Infrastructure.Messaging.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Callu.Tests;

/// <summary>A broker outage has to be visible, and it must not take the host off readiness.</summary>
public class BrokerHealthCheckTests
{
    private static ServiceProvider BuildApp(string host)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("RabbitMQ:Host", host),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCalluMessaging(configuration, CalluMessagingHostRole.ApiPublisher);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task TheBrokerCheck_IsTaggedForTheOperatorSurface_AndNeverForReadiness()
    {
        await using var provider = BuildApp("callu-rabbitmq");

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => r.Name == BrokerHealthCheck.Name);

        Assert.Contains("broker", registration.Tags);
        Assert.Contains("external", registration.Tags);

        // The outbox holds messages while the broker is away; failing readiness would take the UI down too.
        Assert.DoesNotContain("ready", registration.Tags);
    }

    [Fact]
    public async Task AnUnreachableBroker_ReportsUnhealthyRatherThanThrowing()
    {
        await using var provider = BuildApp("a-host-that-does-not-resolve.invalid");

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Tags.Contains("broker"), CancellationToken.None);

        var entry = Assert.Single(report.Entries);
        Assert.Equal(BrokerHealthCheck.Name, entry.Key);
        Assert.Equal(HealthStatus.Unhealthy, entry.Value.Status);
        Assert.NotNull(entry.Value.Exception);
    }

    /// <summary>
    /// The backlog check is not the broker check: the readiness payload reads the "broker" tag, and a
    /// backlog that cannot be counted must not be reported to an operator as an unreachable broker.
    /// </summary>
    [Fact]
    public async Task TheBacklogCheck_IsSeparateFromTheBrokerCheck_AndIsAlsoNeverOnReadiness()
    {
        await using var provider = BuildApp("callu-rabbitmq");

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => r.Name == MessagingBacklogHealthCheck.Name);

        Assert.Contains("messaging", registration.Tags);
        Assert.DoesNotContain("ready", registration.Tags);
        Assert.DoesNotContain("broker", registration.Tags);
    }

    /// <summary>With no broker configured there is no check at all, so nothing reports a phantom outage.</summary>
    [Fact]
    public async Task WithNoBrokerConfigured_NoBrokerCheckIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCalluMessaging(new ConfigurationBuilder().Build(), CalluMessagingHostRole.ApiPublisher);

        await using var provider = services.BuildServiceProvider();

        var registrations = provider.GetService<IOptions<HealthCheckServiceOptions>>()?.Value.Registrations
            ?? [];

        Assert.DoesNotContain(registrations, r => r.Name == BrokerHealthCheck.Name);
    }
}
