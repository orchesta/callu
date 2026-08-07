using Callu.Application.Services;
using Callu.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.DI;

internal static class TracingQueryModule
{
    internal static IServiceCollection AddTracingQueryModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var endpoint = configuration["Observability:TraceQueryUrl"];
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = Environment.GetEnvironmentVariable("JAEGER_QUERY_URL");

        services.AddHttpClient<ITracingQueryService, JaegerTracingQueryService>(client =>
        {
            if (!string.IsNullOrWhiteSpace(endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                client.BaseAddress = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
            }

            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }
}
