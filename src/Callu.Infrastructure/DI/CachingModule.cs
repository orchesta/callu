using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Caching infrastructure — distributed memory cache or Redis + HybridCache (L1+L2)
/// </summary>
internal static class CachingModule
{
    internal static IServiceCollection AddCachingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection.Trim();
                options.InstanceName = "callu:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(2)
            };
        });

        return services;
    }
}
