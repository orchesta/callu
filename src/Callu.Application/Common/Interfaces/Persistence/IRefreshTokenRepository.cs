using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyIdAsync(Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>Atomically claims a single token for rotation; <c>true</c> when this caller won and may
    /// mint the replacement, <c>false</c> when the row was already revoked (treat as reuse).</summary>
    Task<bool> TryRevokeForRotationAsync(
        Guid tokenId,
        DateTime revokedAtUtc,
        string replacedByTokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>Bulk-revoke every active refresh token belonging to a user (the entire rotation chain).
    /// Returns rows affected.</summary>
    Task<int> RevokeAllActiveForUserAsync(string userId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Revoke every active token in one rotation family (one device / one login chain).</summary>
    Task<int> RevokeActiveByFamilyIdAsync(Guid familyId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes refresh tokens whose <c>ExpiresAt</c> is before <paramref name="cutoffUtc"/>.
    /// Only expired rows go; a revoked-but-unexpired one stays so theft-detection still fires on replay.</summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
