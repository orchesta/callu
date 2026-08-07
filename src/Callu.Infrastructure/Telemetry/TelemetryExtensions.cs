using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

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

        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
            otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        if (string.IsNullOrWhiteSpace(otlpEndpoint))
            return services;

        if (!TryResolveOtlpEndpoint(otlpEndpoint, out var endpointUri))
            return services;

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
                        // No url.query enrichment: this instrumentation already replaces every query value
                        // with "Redacted", and overwriting the tag would put the ones it hid back on the span.
                        opts.Filter = httpContext =>
                        {
                            var path = httpContext.Request.Path;

                            if (path.StartsWithSegments("/health"))
                                return false;

                            return path.Value?.Contains("/diagnostics/tracing", StringComparison.OrdinalIgnoreCase) != true;
                        };
                    });
                }

                tracing
                    .AddHttpClientInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        opts.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            if (request.RequestUri is { } uri && SensitiveQueryRedactor.HasSensitiveQuery(uri))
                            {
                                var redacted = SensitiveQueryRedactor.Redact(uri);
                                activity.SetTag("url.full", redacted);
                                activity.SetTag("http.url", redacted);
                            }
                        };
                    })
                    .AddEntityFrameworkCoreInstrumentation(opts =>
                    {
                        opts.SetDbStatementForText = captureDbStatements;
                    })
                    .AddNpgsql()
                    .AddSource(ActivitySourceName);

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
                    .AddMeter(CalluMetrics.MeterName);

                metrics.AddOtlpExporter(opts => opts.Endpoint = endpointUri);
            });

        return services;
    }

    /// <summary>
    /// Turns the configured OTLP value into an absolute http(s) endpoint, or false when unusable.
    /// </summary>
    internal static bool TryResolveOtlpEndpoint(string configured, out Uri endpoint)
    {
        endpoint = null!;

        var trimmed = configured.Trim().TrimEnd('/');
        if (trimmed.Length == 0)
            return false;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) && IsHttp(parsed))
        {
            endpoint = parsed;
            return true;
        }

        if (Uri.TryCreate($"http://{trimmed}", UriKind.Absolute, out var assumed) && IsHttp(assumed))
        {
            Log.Warning(
                "OTLP endpoint '{Configured}' has no scheme; assuming http and exporting to {Endpoint}. "
                + "Set the full URL (OTEL_EXPORTER_OTLP_ENDPOINT / OpenTelemetry:OtlpEndpoint) to silence this.",
                configured, assumed);
            endpoint = assumed;
            return true;
        }

        Log.Warning(
            "OTLP endpoint '{Configured}' is not a usable http/https URL; telemetry export is disabled "
            + "for this host. Traces and metrics will not leave the process.",
            configured);
        return false;
    }

    private static bool IsHttp(Uri uri) =>
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);
}
