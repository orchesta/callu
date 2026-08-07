using Callu.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.Hosting;

/// <summary>
/// Registers Callu hosted services shared by both hosts; periodic jobs live on the Worker only.
/// </summary>
public static class CalluBackgroundServiceExtensions
{
    public static IServiceCollection AddCalluProviderRegistryInitializerHosted(this IServiceCollection services)
    {
        services.AddHostedService<ProviderRegistryInitializer>();
        return services;
    }
}
