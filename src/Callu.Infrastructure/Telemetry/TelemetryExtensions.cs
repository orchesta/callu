using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Callu.Infrastructure.Telemetry;

/// <summary>
/// Registers OpenTelemetry traces and metrics with OTLP export when an endpoint is configured.
/// </summary>
public static class TelemetryExtensions
{
    public const string ActivitySourceName = "Callu";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static IServiceCollection AddCalluTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        CalluTelemetryHostKind hostKind)
    {
        if (!configuration.GetValue("OpenTelemetry:Enabled", true))
            return services;

        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        if (string.IsNullOrWhiteSpace(otlpEndpoint))
            return services;

        var endpointUri = new Uri(otlpEndpoint.TrimEnd('/'));

        var captureDbStatements = configuration.GetValue("OpenTelemetry:CaptureDbStatements", false);
        var instanceId = hostKind == CalluTelemetryHostKind.Api ? "api" : "worker";
        var serviceVersion = typeof(TelemetryExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: "Callu",
                    serviceVersion: serviceVersion,
                    serviceInstanceId: instanceId))
            .WithTracing(tracing =>
            {
                if (hostKind == CalluTelemetryHostKind.Api)
                {
                    tracing.AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        opts.Filter = httpContext =>
                            !httpContext.Request.Path.StartsWithSegments("/health");
                    });
                }

                tracing
                    .AddHttpClientInstrumentation(opts => opts.RecordException = true)
                    .AddEntityFrameworkCoreInstrumentation(opts =>
                    {
                        opts.SetDbStatementForText = captureDbStatements;
                    })
                    .AddNpgsql()
                    .AddSource(ActivitySourceName)
                    .AddSource("MassTransit");

                if (hostKind == CalluTelemetryHostKind.Worker)
                    tracing.AddQuartzInstrumentation();

                tracing.AddOtlpExporter(opts => opts.Endpoint = endpointUri);
            })
            .WithMetrics(metrics =>
            {
                if (hostKind == CalluTelemetryHostKind.Api)
                    metrics.AddAspNetCoreInstrumentation();

                metrics
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddNpgsqlInstrumentation()
                    .AddMeter(CalluMetrics.MeterName)
                    .AddMeter("MassTransit");

                metrics.AddOtlpExporter(opts => opts.Endpoint = endpointUri);
            });

        return services;
    }
}
