using Callu.Application.Messaging;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Messaging.Broker;
using Callu.Infrastructure.Messaging.Consuming;
using Callu.Infrastructure.Messaging.Health;
using Callu.Infrastructure.Messaging.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.Messaging;

/// <summary>Which side of the transport a host runs: the API publishes, the Worker consumes.</summary>
public enum CalluMessagingHostRole
{
    ApiPublisher,
    WorkerConsumer
}

public static class CalluMessagingRegistration
{
    public static IServiceCollection AddCalluMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        CalluMessagingHostRole role)
    {
        var settings = configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>() ?? new RabbitMqSettings();

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            services.AddScoped<IEscalationWorkflowSignal, DirectEscalationWorkflowSignal>();
            services.AddScoped<IStatusPageSubscriberNotifier, DirectStatusPageSubscriberNotifier>();
            return services;
        }

        services.AddSingleton(settings);
        services.AddSingleton<ICalluBrokerConnection, CalluBrokerConnection>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        // Not tagged "ready": the outbox holds messages while the broker is away, so a broker outage
        // must not take the host off the gate that decides whether the UI is served.
        services.AddHealthChecks()
            .AddCheck<BrokerHealthCheck>(BrokerHealthCheck.Name, tags: ["broker", "external"])
            // Its own tag, not "broker": a backlog is a count, not a statement about reachability,
            // and the readiness payload reads the broker tag.
            .AddCheck<MessagingBacklogHealthCheck>(MessagingBacklogHealthCheck.Name, tags: ["messaging"]);

        if (role == CalluMessagingHostRole.ApiPublisher)
        {
            // One instance is the dispatcher, the nudge target and the hosted service, so a commit can wake it.
            services.AddSingleton<OutboxDispatcher>();
            services.AddSingleton<IOutboxNudge>(sp => sp.GetRequiredService<OutboxDispatcher>());
            services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

            services.AddScoped<IEscalationWorkflowSignal, OutboxEscalationWorkflowSignal>();
            services.AddScoped<IStatusPageSubscriberNotifier, OutboxStatusPageSubscriberNotifier>();

            return services;
        }

        // The Worker publishes nothing; escalation it starts itself runs in-process, as it does today.
        services.AddSingleton<IOutboxNudge, NoOpOutboxNudge>();
        services.AddScoped<IEscalationWorkflowSignal, DirectEscalationWorkflowSignal>();
        services.AddScoped<IStatusPageSubscriberNotifier, DirectStatusPageSubscriberNotifier>();

        services.AddScoped<ICalluMessageHandler, TriggerIncidentEscalationHandler>();
        services.AddScoped<ICalluMessageHandler, NotifyStatusPageSubscribersHandler>();
        services.AddScoped<IInboxMessageProcessor, InboxMessageProcessor>();
        services.AddScoped<IInboxRetrySweep, InboxRetrySweep>();
        services.AddHostedService<CalluConsumerHost>();

        return services;
    }
}
