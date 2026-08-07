using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Callu.Infrastructure.Hosting;

/// <summary>The one place a Callu host wires Redis; every host must call both <see cref="AddCalluRedis"/>
/// at registration time and <see cref="UseCalluRedisGuard"/> after the host is built.</summary>
public static class CalluRedisSetup
{
    private const string DataProtectionKeyRingPrefix = "callu:dp-keys";
    private const string SignalRChannelPrefix = "callu:signalr";

    /// <summary>Registers the shared multiplexer and the Data Protection key ring; null means no Redis
    /// is configured, which is a supported single-host setup.</summary>
    public static IConnectionMultiplexer? AddCalluRedis(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        var connectionString = configuration.GetConnectionString("Redis")?.Trim();

        IConnectionMultiplexer? multiplexer = null;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            multiplexer = CreateMultiplexer(connectionString);
            services.AddSingleton(multiplexer);
        }

        var dataProtection = services.AddDataProtection()
            .SetApplicationName("CalluApp")
            .SetDefaultKeyLifetime(TimeSpan.FromDays(365));

        if (multiplexer is not null)
        {
            dataProtection.PersistKeysToStackExchangeRedis(multiplexer, DataProtectionKeyRingPrefix);
        }
        else
        {
            dataProtection.PersistKeysToFileSystem(
                new DirectoryInfo(Path.Combine(contentRootPath, "keys")));
        }

        return multiplexer;
    }

    /// <summary>The SignalR backplane, on the same channel prefix on every host.</summary>
    public static ISignalRServerBuilder AddCalluSignalRBackplane(
        this ISignalRServerBuilder signalR, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            return signalR;

        return signalR.AddStackExchangeRedis(connectionString, options =>
        {
            options.Configuration.ChannelPrefix = RedisChannel.Literal(SignalRChannelPrefix);
        });
    }

    /// <summary>
    /// Runs the Redis configuration guard against the built host, so its verdict reaches the sinks
    /// the operator actually reads.
    /// </summary>
    public static void UseCalluRedisGuard(this IHost host)
    {
        WarnAboutRedisConfiguration(
            host.Services.GetRequiredService<IConfiguration>(),
            host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Callu.Startup"));
    }

    private static IConnectionMultiplexer CreateMultiplexer(string connectionString)
    {
        ConfigurationOptions redisOptions;
        try
        {
            redisOptions = ConfigurationOptions.Parse(connectionString);
        }
        catch (Exception ex)
        {
            // Deliberately fatal. Falling back to the filesystem key ring would strand the key
            // ring already in Redis and leave the stored SMTP / SIP passwords undecryptable —
            // far worse than refusing to start.
            throw new InvalidOperationException(
                "ConnectionStrings:Redis could not be parsed. In Docker it is assembled from " +
                "REDIS_HOST / REDIS_PORT / REDIS_PASSWORD, so a ',' or '=' in the password will " +
                "corrupt it; set REDIS_CONNECTION_STRING to a full connection string instead.", ex);
        }

        // Redis is optional — never block startup on it.
        redisOptions.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(redisOptions);
    }

    /// <summary>
    /// Reports Redis misconfigurations that are otherwise silent, because AbortOnConnectFail is
    /// false and a broken Redis therefore looks exactly like a healthy one at startup.
    /// </summary>
    public static void WarnAboutRedisConfiguration(IConfiguration configuration, ILogger logger)
    {
        var connectionString = configuration.GetConnectionString("Redis")?.Trim();

        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        ConfigurationOptions redisOptions;
        try
        {
            redisOptions = ConfigurationOptions.Parse(connectionString);
        }
        catch
        {
            return; // CreateMultiplexer already fails loudly on this
        }

        if (redisOptions.EndPoints.Count > 1)
        {
            logger.LogWarning(
                "The Redis connection string resolved to {Count} endpoints. If you did not configure a " +
                "cluster on purpose, REDIS_PASSWORD almost certainly contains a ',' — a comma separates " +
                "options in a Redis connection string and cannot be escaped, so part of the password is " +
                "being read as an extra server. Use a password with no commas or spaces.",
                redisOptions.EndPoints.Count);
        }

        if (string.IsNullOrEmpty(redisOptions.Password))
        {
            logger.LogWarning(
                "The Redis connection string carries no password. If your Redis runs with requirepass — " +
                "the bundled one does — every command is rejected with NOAUTH and HybridCache L2, the " +
                "SignalR backplane and the Data Protection key ring all stop working silently. Check the " +
                "'redis' entry on /health/detail for the live status.");
        }
    }
}
