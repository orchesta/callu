using Callu.Application.Messaging;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Messaging.Consumers;
using Callu.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.Messaging;

/// <summary>
/// MassTransit + RabbitMQ registration for API (publish-only) or Worker (consumers).
/// </summary>
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
        var enabled = !string.IsNullOrWhiteSpace(settings.Host);

        if (!enabled)
        {
            services.AddScoped<IEscalationWorkflowSignal, DirectEscalationWorkflowSignal>();
            services.AddScoped<IStatusPageSubscriberNotifier, DirectStatusPageSubscriberNotifier>();
            return services;
        }

        if (role == CalluMessagingHostRole.ApiPublisher)
        {
            services.AddScoped<IEscalationWorkflowSignal, MassTransitEscalationWorkflowSignal>();
            services.AddScoped<IStatusPageSubscriberNotifier, MassTransitStatusPageSubscriberNotifier>();
        }
        else
        {
            services.AddScoped<IEscalationWorkflowSignal, DirectEscalationWorkflowSignal>();
            services.AddScoped<IStatusPageSubscriberNotifier, DirectStatusPageSubscriberNotifier>();
        }

        services.AddMassTransit(bus =>
        {
            if (role == CalluMessagingHostRole.ApiPublisher)
            {
                bus.AddEntityFrameworkOutbox<ApplicationDbContext>(o =>
                {
                    o.UsePostgres();
                    o.QueryDelay = TimeSpan.FromSeconds(1);
                    o.DuplicateDetectionWindow = TimeSpan.FromMinutes(5);
                    o.UseBusOutbox();
                });
            }

            if (role == CalluMessagingHostRole.WorkerConsumer)
            {
                bus.AddEntityFrameworkOutbox<ApplicationDbContext>(o =>
                {
                    o.UsePostgres();
                    o.QueryDelay = TimeSpan.FromSeconds(1);
                    o.DuplicateDetectionWindow = TimeSpan.FromMinutes(5);
                });

                bus.AddConfigureEndpointsCallback((context, _, cfg) =>
                {
                    cfg.UseMessageRetry(r => r.Intervals(
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromMilliseconds(500),
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(30),
                        TimeSpan.FromMinutes(2)));
                    cfg.UseEntityFrameworkOutbox<ApplicationDbContext>(context);
                });

                bus.AddConsumer<TriggerIncidentEscalationConsumer>();
                bus.AddConsumer<NotifyStatusPageSubscribersConsumer>();
            }

            bus.UsingRabbitMq((context, cfg) =>
            {
                var vhost = string.IsNullOrWhiteSpace(settings.VirtualHost) ? "/" : settings.VirtualHost!;
                cfg.Host(settings.Host, vhost, h =>
                {
                    h.Username(settings.Username ?? "guest");
                    h.Password(settings.Password ?? "guest");
                });

                if (role == CalluMessagingHostRole.WorkerConsumer)
                    cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
