namespace Callu.Application.Common.Interfaces;

/// <summary>
/// Short-lived blacklist of revoked access-token jtis.
/// Backed by <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> — entries
/// live only until the token would have naturally expired, so the store stays small
/// (bounded by access-token TTL) and clustered deployments share state via Redis L2.
/// </summary>
public interface IAccessTokenRevocationStore
{
    /// <summary>
    /// Mark a token as revoked. <paramref name="ttl"/> should match the remaining
    /// validity of the access token; after it elapses the token expires on its own
    /// and the blacklist entry can be dropped.
    /// </summary>
    Task RevokeAsync(string jti, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if the jti has been revoked and the entry has not yet expired.
    /// </summary>
    Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default);
}
