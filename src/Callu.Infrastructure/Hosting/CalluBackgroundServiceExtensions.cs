using Callu.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.Hosting;

/// <summary>
/// Registers Callu hosted background jobs (escalation, notification queue, health checks, provider registry).
/// </summary>
public static class CalluBackgroundServiceExtensions
{
    /// <summary>
    /// Timer-based periodic jobs (API local dev when Worker is not used).
    /// </summary>
    public static IServiceCollection AddCalluPeriodicBackgroundServices(this IServiceCollection services)
    {
        services.AddHostedService<EscalationBackgroundService>();
        services.AddHostedService<NotificationBackgroundService>();
        services.AddHostedService<HealthCheckBackgroundService>();
        services.AddHostedService<ScheduleMaterializationBackgroundService>();
        return services;
    }

    public static IServiceCollection AddCalluProviderRegistryInitializerHosted(this IServiceCollection services)
    {
        services.AddHostedService<ProviderRegistryInitializer>();
        return services;
    }

    public static IServiceCollection AddCalluBackgroundServices(this IServiceCollection services)
    {
        services.AddCalluPeriodicBackgroundServices();
        services.AddCalluProviderRegistryInitializerHosted();
        return services;
    }
}
