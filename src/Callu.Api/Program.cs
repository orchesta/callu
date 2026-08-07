using Callu.Api;
using Callu.Api.Configuration;
using Callu.Infrastructure.SignalR;
using Callu.Api.Middleware;
using Callu.Application;
using Callu.Infrastructure;
using Callu.Infrastructure.Hosting;
using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Results;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NodaTime.Serialization.SystemTextJson;
using Callu.Infrastructure.Audit;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With<ActivityTraceEnricher>()
    .WriteToAuditTrailStream(context.Configuration));

builder.Services.AddApplication();
builder.Services.AddCalluTelemetry(builder.Configuration, CalluTelemetryHostKind.Api);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCalluMessaging(builder.Configuration, CalluMessagingHostRole.ApiPublisher);

builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddApiServices();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<Callu.Api.Filters.FluentValidationFilter>();
    options.Filters.Add<Callu.Api.Filters.ApiResponseWrapperFilter>();
}).ConfigureApiBehaviorOptions(options =>
{
    // Keep [ApiController] model-binding/DataAnnotation errors in the same ApiResponse
    // envelope (and per-field shape) that FluentValidation failures produce.
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .GroupBy(entry => string.IsNullOrEmpty(entry.Key) ? "General" : entry.Key)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(entry => entry.Value!.Errors.Select(error =>
                        string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "The value provided is invalid."
                            : error.ErrorMessage))
                    .ToArray());

        return new BadRequestObjectResult(
            ApiResponse.Fail<object>("One or more validation errors occurred.", errors));
    };
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.JsonSerializerOptions.ConfigureForNodaTime(NodaTime.DateTimeZoneProviders.Tzdb);
});

builder.Services.AddVersionedSwagger();

builder.Services.AddApiInfrastructure(builder);

builder.Services.Configure<Callu.Api.Middleware.VoximplantSignatureOptions>(
    builder.Configuration.GetSection(Callu.Api.Middleware.VoximplantSignatureOptions.SectionName));

var app = builder.Build();

// Surfaces Redis misconfigurations that are otherwise silent — AbortOnConnectFail is false, so a
// broken Redis looks healthy at boot while the Data Protection key ring (and every stored SMTP /
// SIP secret) is gone. The Worker runs the same guard — see Callu.Infrastructure/Hosting/CalluRedisSetup.cs.
app.UseCalluRedisGuard();

Callu.Shared.Localization.Messages.Initialize(
    Path.Combine(builder.Environment.ContentRootPath, "Resources", "Locales"));

Callu.Shared.Localization.TtsDefaults.Initialize(
    Path.Combine(builder.Environment.ContentRootPath, "Resources", "TtsDefaults"));

await app.InitializeDatabaseAsync();

app.UseForwardedHeaders();

// Ahead of the exception handler on purpose: CurrentUICulture is async-local, so a culture set
// further down the pipeline is not visible to the handler that formats the error message.
// UI cultures only — SupportedCultures is left alone so request headers cannot change number
// or date formatting.
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture(Callu.Shared.Localization.Messages.FallbackLanguage)
    .AddSupportedUICultures([.. Callu.Shared.Localization.Messages.AvailableLanguages]));
app.UseVersionedSwagger();
// RequestPath is the path alone while IncludeQueryInRequestPath stays off, and the whole request target
// the moment it is turned on. It is written at Error level for every 5xx, so it is redacted either way.
app.UseSerilogRequestLogging(options => options.GetMessageTemplateProperties = Callu.Api.Configuration.RequestLoggingProperties.Get);
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");
// No UseHsts(): SecurityHeadersMiddleware already writes the header, and the two together
// emit it twice with different values.
app.UseSecurityHeaders();
app.UseRateLimiter();
app.UseMiddleware<Callu.Api.Middleware.VoximplantSignatureMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// The container probe, and the gate callu-web waits on. Only "ready"-tagged checks run here, so a
// 503 always means the API itself cannot serve — optional dependencies report on /health/detail.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).DisableHttpMetrics().AllowAnonymous();

// Everything, with per-check detail. nginx does not proxy this path (it enumerates dependencies
// and their failure reasons); read it with `docker exec callu-api curl -s localhost:5095/health/detail`.
app.MapHealthChecks("/health/detail", new HealthCheckOptions
{
    ResponseWriter = WriteHealthReport
}).DisableHttpMetrics().AllowAnonymous();

app.MapHub<NotificationHub>("/hubs/notifications");

static Task WriteHealthReport(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            error = entry.Value.Exception?.Message,
            durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
            tags = entry.Value.Tags,
            // Counts a check wants read rather than parsed out of its sentence; omitted when it has none.
            data = entry.Value.Data.Count == 0 ? null : entry.Value.Data
        })
    });
}

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");

    // A startup that refused to start must LOOK like a failure. Exit 0 tells Docker/k8s/systemd the
    // container did its job and stopped cleanly: `restart: unless-stopped` leaves it down, the
    // restart backoff never engages, and nothing pages anyone about it.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
