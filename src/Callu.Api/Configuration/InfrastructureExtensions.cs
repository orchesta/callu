using System.Net;
using System.Threading.RateLimiting;
using Callu.Api.ExceptionHandlers;
using Callu.Application.Services;
using Callu.Infrastructure.Hosting;
using Callu.Infrastructure.SignalR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using StackExchange.Redis;

namespace Callu.Api.Configuration;

/// <summary>
/// Extension methods for configuring infrastructure concerns:
/// CORS, SignalR, health checks, background services, data protection, rate limiting.
/// </summary>
public static class InfrastructureExtensions
{
    public static IServiceCollection AddApiInfrastructure(
        this IServiceCollection services, WebApplicationBuilder builder)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                }
                else
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                }
            });
        });

        var redisConnection = builder.Configuration.GetConnectionString("Redis");
        var signalR = services.AddSignalR();
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            signalR.AddStackExchangeRedis(redisConnection.Trim(), options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("callu:signalr");
            });
        }

        services.AddScoped<INotificationPushService, SignalRNotificationPushService>();

        services.AddHealthChecks()
            .AddCheck<Callu.Infrastructure.Health.DatabaseHealthCheck>("postgresql", tags: new[] { "db", "ready" })
            .AddCheck<Callu.Infrastructure.Health.SmtpHealthCheck>("smtp", tags: new[] { "email" })
            .AddResourceUtilizationHealthCheck()
            .AddApplicationLifecycleHealthCheck();

        if (builder.Configuration.GetValue("Callu:EnableBackgroundServices", true))
            services.AddCalluBackgroundServices();

        var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
        var dpBuilder = services.AddDataProtection()
            .SetApplicationName("CalluApp")
            .SetDefaultKeyLifetime(TimeSpan.FromDays(365));

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            var redis = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString);
            dpBuilder.PersistKeysToStackExchangeRedis(redis, "callu:dp-keys");
        }
        else
        {
            var keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "keys");
            dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
        }

        services.AddOptions<ForwardedHeadersOptions>().Configure<ILogger<ForwardedHeadersOptions>>((options, logger) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;

            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            var trustedCidrs = builder.Configuration
                .GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>()
                ?? ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fd00::/8"];
            foreach (var cidr in trustedCidrs)
            {
                if (System.Net.IPNetwork.TryParse(cidr, out var network))
                    options.KnownIPNetworks.Add(network);
                else
                    logger.LogWarning("Ignoring invalid ForwardedHeaders:KnownNetworks entry '{Cidr}' — not a valid CIDR", cidr);
            }

            var trustedProxies = builder.Configuration
                .GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
            foreach (var proxy in trustedProxies)
            {
                if (IPAddress.TryParse(proxy, out var ip))
                    options.KnownProxies.Add(ip);
                else
                    logger.LogWarning("Ignoring invalid ForwardedHeaders:KnownProxies entry '{Proxy}' — not a valid IP address", proxy);
            }
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;

            options.AddPolicy("webhook", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 10,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));

            options.AddPolicy("callback", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 200,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            options.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: GetClientIp(httpContext),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        SegmentsPerWindow = 5
                    }));

            options.AddPolicy("statuspage_subscribe", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"{GetClientIp(httpContext)}|{httpContext.Request.RouteValues["pageId"]}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            options.AddPolicy("statuspage_view", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            options.AddPolicy("statuspage_public", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"{GetClientIp(httpContext)}|{httpContext.Request.RouteValues["slug"] ?? httpContext.Request.RouteValues["pageId"]}",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6
                    }));
        });

        return services;
    }

    private static string GetClientIp(HttpContext httpContext)
    {
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
