using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyIdAsync(Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-revoke every active refresh token belonging to a user (entire rotation
    /// chain). Used by password change / reset / role change / user removal so a
    /// credential-affecting event invalidates every session. Single SQL UPDATE via
    /// <c>ExecuteUpdateAsync</c>. Returns rows affected.
    /// </summary>
    Task<int> RevokeAllActiveForUserAsync(string userId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes refresh tokens whose <c>ExpiresAt</c> is before <paramref name="cutoffUtc"/>.
    /// Only expired rows are removed — a revoked-but-unexpired token is kept until it expires so
    /// family theft-detection still fires if it is replayed. Bounds unbounded table growth.
    /// Single SQL DELETE via <c>ExecuteDeleteAsync</c>. Returns rows affected.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
