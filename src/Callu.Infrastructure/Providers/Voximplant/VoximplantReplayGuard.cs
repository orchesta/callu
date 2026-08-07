using Callu.Application.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>Distributed nonce cache for VoxEngine callback replay protection, so replicas share nonce
/// state when Redis is configured.</summary>
public sealed class VoximplantReplayGuard(
    IDistributedCache cache,
    IOptions<VoximplantReplayGuardOptions> options) : IVoximplantReplayGuard
{
    private const string KeyPrefix = "vox:replay:";
    private static readonly byte[] Marker = [1];

    private readonly int _windowSeconds = options.Value.WindowSeconds;

    public int WindowSeconds => _windowSeconds;

    public bool TryRegister(long unixTimestampSeconds, string nonce)
    {
        if (string.IsNullOrWhiteSpace(nonce))
            return false;

        var key = KeyPrefix + unixTimestampSeconds + ":" + nonce;

        if (cache.Get(key) is not null)
            return false;

        cache.Set(key, Marker, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_windowSeconds),
        });
        return true;
    }
}
