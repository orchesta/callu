namespace Callu.Application.Common.Interfaces;

/// <summary>Short-lived blacklist of revoked access-token jtis, backed by <c>HybridCache</c>.</summary>
public interface IAccessTokenRevocationStore
{
    /// <summary>Mark a token as revoked; <paramref name="ttl"/> should match the access token's
    /// remaining validity.</summary>
    Task RevokeAsync(string jti, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// True if the jti has been revoked and the entry has not yet expired.
    /// </summary>
    Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default);
}
