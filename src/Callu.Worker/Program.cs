using Callu.Application;
using Callu.Application.Common.Interfaces;
using Callu.Infrastructure;
using Callu.Infrastructure.Hosting;
using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Worker.Quartz;
using Callu.Shared.Localization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var healthPort = builder.Configuration.GetValue("Worker:HealthPort", 8080);
    builder.WebHost.UseUrls($"http://+:{healthPort}");

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.With<ActivityTraceEnricher>());

    builder.Services.AddApplication();
    builder.Services.AddCalluTelemetry(builder.Configuration, CalluTelemetryHostKind.Worker);

    builder.Services.AddScoped<ICurrentUserService, SystemCurrentUserService>();

    builder.Services.AddInfrastructure(builder.Configuration);

    var workerDpRedis = builder.Configuration.GetConnectionString("Redis");
    var workerDp = builder.Services.AddDataProtection()
        .SetApplicationName("CalluApp")
        .SetDefaultKeyLifetime(TimeSpan.FromDays(365));

    if (!string.IsNullOrWhiteSpace(workerDpRedis))
    {
        var dpRedisMux = StackExchange.Redis.ConnectionMultiplexer.Connect(workerDpRedis);
        workerDp.PersistKeysToStackExchangeRedis(dpRedisMux, "callu:dp-keys");
    }
    else
    {
        var keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "keys");
        workerDp.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
    }

    var workerRedis = builder.Configuration.GetConnectionString("Redis");
    var workerSignalR = builder.Services.AddSignalR();
    if (!string.IsNullOrWhiteSpace(workerRedis))
    {
        workerSignalR.AddStackExchangeRedis(workerRedis.Trim(), options =>
        {
            options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("callu:signalr");
        });
    }
    builder.Services.AddScoped<Callu.Application.Services.INotificationPushService,
                                Callu.Infrastructure.SignalR.SignalRNotificationPushService>();

    var workerRabbitHost = builder.Configuration["RabbitMQ:Host"];
    if (!string.IsNullOrWhiteSpace(workerRabbitHost) && string.IsNullOrWhiteSpace(workerRedis))
    {
        Log.Warning(
            "Worker is in multi-host mode (RabbitMQ configured) without a Redis backplane " +
            "(ConnectionStrings:Redis is empty). Incident updates made in the Worker will NOT " +
            "reach connected UI clients in real time — they surface only on the client's next " +
            "refetch. Set ConnectionStrings:Redis on BOTH the API and Worker to enable the " +
            "SignalR backplane.");
    }

    builder.Services.AddCalluMessaging(builder.Configuration, CalluMessagingHostRole.WorkerConsumer);
    builder.Services.AddCalluProviderRegistryInitializerHosted();
    builder.Services.AddCalluWorkerQuartzScheduling(builder.Configuration);

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var workerDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workerLogger = app.Services.GetRequiredService<ILogger<Program>>();
        await MigrationRunner.RunAsync(workerDb, workerLogger);
    }

    Messages.Initialize(
        Path.Combine(builder.Environment.ContentRootPath, "Resources", "Locales", "en.json"));
    TtsDefaults.Initialize(
        Path.Combine(builder.Environment.ContentRootPath, "Resources", "TtsDefaults"));

    app.MapGet("/health/live", () => Results.Ok(new
    {
        status = "Alive",
        timestamp = DateTime.UtcNow
    }));

    app.MapGet("/health/ready", async (
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<Program> logger,
        CancellationToken ct) =>
    {
        bool dbOk;
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct);
            dbOk = await context.Database.CanConnectAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Worker health probe: database unreachable");
            dbOk = false;
        }

        var payload = new
        {
            status = dbOk ? "Ready" : "Unhealthy",
            database = dbOk ? "Connected" : "Disconnected",
            timestamp = DateTime.UtcNow
        };
        return dbOk ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Callu.Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
