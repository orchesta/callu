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
using Microsoft.EntityFrameworkCore;
using Callu.Infrastructure.Audit;
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
        .Enrich.With<ActivityTraceEnricher>()
        .WriteToAuditTrailStream(builder.Configuration));

    builder.Services.AddApplication();
    builder.Services.AddCalluTelemetry(builder.Configuration, CalluTelemetryHostKind.Worker);

    builder.Services.AddScoped<ICurrentUserService, SystemCurrentUserService>();

    builder.Services.AddInfrastructure(builder.Configuration);

    // Redis — multiplexer, Data Protection key ring, and the startup guard — exactly as the API
    // wires it. The Worker used to parse the connection string by hand and got none of the guards:
    // pointed at the wrong Redis it found no key ring, so the stored SMTP and SIP passwords never
    // decrypted and email and voice paging died silently, on the ONE host that pages.
    builder.Services.AddCalluRedis(builder.Configuration, builder.Environment.ContentRootPath);

    var workerRedis = builder.Configuration.GetConnectionString("Redis");
    builder.Services.AddSignalR().AddCalluSignalRBackplane(builder.Configuration);
    builder.Services.AddScoped<Callu.Infrastructure.SignalR.SignalRNotificationPushService>();
    builder.Services.AddScoped<Callu.Application.Services.INotificationPushService,
                                Callu.Infrastructure.SignalR.CompositeNotificationPushService>();

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

    var workerPersistentStore = builder.Configuration.GetValue("Quartz:UsePersistentStore", false);
    if (!workerPersistentStore)
    {
        // Fires for every in-memory start — including no-broker dual Workers, which the old
        // RabbitMQ-gated copy never warned about. Clustering is the only supported multi-replica path.
        Log.Warning(
            "Quartz is using the in-memory job store (Quartz:UsePersistentStore is false). " +
            "Run exactly one Worker replica: a second process fires EVERY job independently — " +
            "escalations page twice, retries double-send. For more than one Worker, set " +
            "Quartz:UsePersistentStore=true and create the qrtz_* tables so clustered Workers share " +
            "one schedule.");

        // Redis TTL lease: a second replica on the same key ring gets a runtime Warning too.
        // Skipped when Redis is empty (startup copy above still applies).
        builder.Services.AddHostedService<Callu.Worker.Hosting.WorkerQuartzReplicaGuard>();
    }

    builder.Services.AddCalluMessaging(builder.Configuration, CalluMessagingHostRole.WorkerConsumer);
    builder.Services.AddCalluProviderRegistryInitializerHosted();
    builder.Services.AddCalluWorkerQuartzScheduling(builder.Configuration);

    // Must stay below docker-compose's stop_grace_period for callu-worker.
    builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(45));

    var app = builder.Build();

    // Same guard as the API, and it matters more here: the Worker is the only host that pages, so a
    // Redis it cannot decrypt secrets from means it reaches nobody. Run after Build so the verdict
    // reaches the operator's configured log sinks, and before the migration so a host that is going
    // to refuse does not touch the database first.
    app.UseCalluRedisGuard();

    using (var scope = app.Services.CreateScope())
    {
        var workerDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workerLogger = app.Services.GetRequiredService<ILogger<Program>>();
        await MigrationRunner.RunAsync(workerDb, workerLogger);
    }

    Messages.Initialize(
        Path.Combine(builder.Environment.ContentRootPath, "Resources", "Locales"));
    foreach (var failure in TtsDefaults.Initialize(
                 Path.Combine(builder.Environment.ContentRootPath, "Resources", "TtsDefaults")))
    {
        app.Logger.LogError(
            "Spoken defaults for {LanguageCode} did not load ({Reason}), so a call in that language "
            + "will fall back to English wording", failure.LanguageCode, failure.Reason);
    }

    app.MapGet("/health/live", () => Results.Ok(new
    {
        status = "Alive",
        timestamp = DateTime.UtcNow
    }));

    // Redis is reported but never fails the probe: AbortOnConnectFail is false, so a wrong password
    // (NOAUTH) or a dead Redis is otherwise completely invisible — and a Redis blip must not restart
    // the host that runs escalation. Mirrors the API's "external" health-check tag.
    app.MapGet("/health/ready", async (
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IServiceProvider services,
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

        // The scheduler IS this host: every job that pages runs on it, so a stopped or shut-down
        // scheduler is a Worker that looks alive and does nothing. Broker is reported, never fatal.
        var schedulerOk = await ProbeSchedulerAsync(services, logger, ct);
        var broker = await DescribeBrokerAsync(services, logger, ct);

        var ready = dbOk && schedulerOk;
        var payload = new
        {
            status = ready ? "Ready" : "Unhealthy",
            database = dbOk ? "Connected" : "Disconnected",
            scheduler = schedulerOk ? "Running" : "Stopped",
            broker,
            redis = await DescribeRedisAsync(services),
            timestamp = DateTime.UtcNow
        };
        return ready ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Callu.Worker terminated unexpectedly");

    // A Worker that refused to start must LOOK failed. Exit 0 reads as a clean shutdown to Docker,
    // k8s and systemd — the restart policy never engages and nobody is told that the one host that
    // pages is gone.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

static async Task<bool> ProbeSchedulerAsync(IServiceProvider services, ILogger<Program> logger, CancellationToken ct)
{
    var factory = services.GetService<Quartz.ISchedulerFactory>();
    if (factory is null)
    {
        logger.LogError("Worker health probe: no Quartz scheduler is registered, so nothing periodic runs on this host");
        return false;
    }

    try
    {
        var scheduler = await factory.GetScheduler(ct);
        var ok = scheduler.IsStarted && !scheduler.IsShutdown && !scheduler.InStandbyMode;
        if (!ok)
            logger.LogError(
                "Worker health probe: scheduler is not running (started={Started}, shutdown={Shutdown}, standby={Standby})",
                scheduler.IsStarted, scheduler.IsShutdown, scheduler.InStandbyMode);
        return ok;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Worker health probe: could not reach the Quartz scheduler");
        return false;
    }
}

static async Task<string> DescribeBrokerAsync(IServiceProvider services, ILogger<Program> logger, CancellationToken ct)
{
    if (services.GetService<Callu.Infrastructure.Messaging.Broker.ICalluBrokerConnection>() is null)
        return "NotConfigured";

    var health = services.GetService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
    if (health is null)
        return "Unknown";

    var report = await health.CheckHealthAsync(r => r.Tags.Contains("broker"), ct);
    if (report.Entries.Count == 0)
        return "Unknown";

    if (report.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy)
        return "Connected";

    // Loud, because the symptom is silent: the API's publishes land on an exchange with no bound
    // queue, and every new incident waits for the reconcile sweep instead of being paged at once.
    var detail = report.Entries.Values.FirstOrDefault().Description ?? report.Status.ToString();
    logger.LogError("Worker health probe: broker unusable ({Detail}). Escalation triggers are not being consumed", detail);
    return $"Unusable: {detail}";
}

static async Task<string> DescribeRedisAsync(IServiceProvider services)
{
    var multiplexer = services.GetService<StackExchange.Redis.IConnectionMultiplexer>();
    if (multiplexer is null)
        return "NotConfigured";

    try
    {
        var latency = await multiplexer.GetDatabase().PingAsync();
        return $"Connected ({latency.TotalMilliseconds:F0} ms)";
    }
    catch (Exception ex)
    {
        // Named loudly: without Redis the Data Protection key ring is unreachable, so the stored
        // SMTP and SIP passwords do not decrypt and this host pages nobody.
        return $"Unusable: {ex.Message}";
    }
}
