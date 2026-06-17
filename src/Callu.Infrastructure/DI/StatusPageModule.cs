using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Status page services — pages, components, health check probing
/// </summary>
internal static class StatusPageModule
{
    internal static IServiceCollection AddStatusPageModule(this IServiceCollection services)
    {
        services.AddScoped<Application.Services.IStatusPageService, Services.StatusPageService>();
        services.AddScoped<Application.Services.IStatusPageComponentService, Services.StatusPageComponentService>();
        services.AddScoped<Application.Services.IStatusPageSubscriberEmailSender, Services.StatusPageSubscriberEmailSender>();

        services.AddScoped<Application.Services.IHealthCheckResponseParser, Services.HealthCheckResponseParser>();
        services.AddScoped<Application.Services.IHealthCheckExecutor, Services.HealthCheckExecutor>();

        return services;
    }
}
