using Microsoft.Extensions.Caching.Distributed;
using Callu.Application.Common.Interfaces;

namespace Callu.Infrastructure.Services;

/// <summary>
/// <see cref="IDistributedCache"/>-backed revocation store. The entry's absolute
/// expiration is set to the access-token's remaining lifetime, so the blacklist
/// empties naturally without requiring a cleanup job. With Redis configured, entries
/// are visible across API replicas immediately; without Redis each process has its
/// own in-memory view (DistributedMemoryCache) — acceptable for single-host
/// deployments and still far better than no revocation at all.
/// </summary>
public sealed class AccessTokenRevocationStore(IDistributedCache cache) : IAccessTokenRevocationStore
{
    private const string KeyPrefix = "jwt:revoked:";
    private static readonly byte[] RevokedMarker = [0x01];

    public async Task RevokeAsync(string jti, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jti) || ttl <= TimeSpan.Zero)
            return;

        await cache.SetAsync(
            KeyPrefix + jti,
            RevokedMarker,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            cancellationToken);
    }

    public async Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return false;

        var value = await cache.GetAsync(KeyPrefix + jti, cancellationToken);
        return value is not null;
    }
}
