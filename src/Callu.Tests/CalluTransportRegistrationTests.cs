using Callu.Application.Messaging;
using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Messaging.Broker;
using Callu.Infrastructure.Messaging.Consuming;
using Callu.Infrastructure.Messaging.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Callu.Tests;

/// <summary>What each host registers, and that registration alone opens no socket.</summary>
public class CalluTransportRegistrationTests
{
    private static ServiceCollection Register(CalluMessagingHostRole role, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddCalluMessaging(configuration, role);
        return services;
    }

    private static bool Has<TService>(ServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(TService));

    private static bool HasImplementation<TImplementation>(ServiceCollection services) =>
        services.Any(d => d.ImplementationType == typeof(TImplementation));

    [Theory]
    [InlineData(CalluMessagingHostRole.ApiPublisher)]
    [InlineData(CalluMessagingHostRole.WorkerConsumer)]
    public void WithNoBroker_NeitherHostRegistersATransport(CalluMessagingHostRole role)
    {
        var services = Register(role);

        Assert.False(Has<ICalluBrokerConnection>(services));
        Assert.False(Has<IOutboxWriter>(services));
        Assert.False(Has<IInboxMessageProcessor>(services));
        Assert.False(HasImplementation<CalluConsumerHost>(services));

        // The in-process path is what an install without a broker runs on.
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IEscalationWorkflowSignal)
            && d.ImplementationType == typeof(DirectEscalationWorkflowSignal));
    }

    [Fact]
    public void TheApi_StagesAndDispatches_ButDoesNotConsume()
    {
        var services = Register(
            CalluMessagingHostRole.ApiPublisher,
            ("RabbitMQ:Host", "callu-rabbitmq"));

        Assert.True(Has<IOutboxWriter>(services));
        Assert.True(Has<IOutboxNudge>(services));
        Assert.True(Has<ICalluBrokerConnection>(services));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IEscalationWorkflowSignal)
            && d.ImplementationType == typeof(OutboxEscalationWorkflowSignal));

        Assert.False(Has<IInboxMessageProcessor>(services));
        Assert.False(Has<IInboxRetrySweep>(services));
        Assert.False(HasImplementation<CalluConsumerHost>(services));
    }

    [Fact]
    public void TheWorker_ConsumesAndSweeps_ButPublishesNothing()
    {
        var services = Register(
            CalluMessagingHostRole.WorkerConsumer,
            ("RabbitMQ:Host", "callu-rabbitmq"));

        Assert.True(Has<IInboxMessageProcessor>(services));
        Assert.True(Has<IInboxRetrySweep>(services));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(ICalluMessageHandler)));

        // Escalation the Worker starts itself stays in-process; only the API publishes.
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IEscalationWorkflowSignal)
            && d.ImplementationType == typeof(DirectEscalationWorkflowSignal));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(OutboxDispatcher));
    }

    /// <summary>The nudge, the dispatcher and the hosted service must be one object, or a commit wakes nothing.</summary>
    [Fact]
    public async Task TheApisDispatcher_NudgeAndHostedService_AreTheSameInstance()
    {
        var services = Register(
            CalluMessagingHostRole.ApiPublisher,
            ("RabbitMQ:Host", "callu-rabbitmq"));
        services.AddLogging();

        await using var provider = services.BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();
        Assert.Same(dispatcher, provider.GetRequiredService<IOutboxNudge>());
        Assert.Contains(dispatcher, provider.GetServices<IHostedService>());
    }

    /// <summary>Registration must be pure: a broker that is down cannot stop either host from starting.</summary>
    [Fact]
    public async Task Registering_OpensNoConnection()
    {
        var services = Register(
            CalluMessagingHostRole.ApiPublisher,
            ("RabbitMQ:Host", "a-host-that-does-not-resolve.invalid"));
        services.AddLogging();

        await using var provider = services.BuildServiceProvider();

        // Resolving the connection object is not connecting; the first channel is.
        Assert.NotNull(provider.GetRequiredService<ICalluBrokerConnection>());
    }
}
