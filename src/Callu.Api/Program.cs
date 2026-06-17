using Callu.Api;
using Callu.Api.Configuration;
using Callu.Infrastructure.SignalR;
using Callu.Api.Middleware;
using Callu.Application;
using Callu.Infrastructure;
using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Telemetry;
using NodaTime.Serialization.SystemTextJson;
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
    .Enrich.With<ActivityTraceEnricher>());

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

Callu.Shared.Localization.Messages.Initialize(
    Path.Combine(builder.Environment.ContentRootPath, "Resources", "Locales", "en.json"));

Callu.Shared.Localization.TtsDefaults.Initialize(
    Path.Combine(builder.Environment.ContentRootPath, "Resources", "TtsDefaults"));

await app.InitializeDatabaseAsync();

app.UseForwardedHeaders();
app.UseVersionedSwagger();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");
app.UseSecurityHeaders();
app.UseHsts();
app.UseRateLimiter();
app.UseMiddleware<Callu.Api.Middleware.VoximplantSignatureMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").DisableHttpMetrics();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
