using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository(ApplicationDbContext context, ILogger<RefreshTokenRepository> logger)
    : Repository<RefreshToken>(context, logger), IRefreshTokenRepository
{
    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        await _dbSet.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        await _dbSet
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyIdAsync(
        Guid familyId,
        CancellationToken cancellationToken = default) =>
        await _dbSet
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public async Task<int> RevokeAllActiveForUserAsync(
        string userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var revoked = await _dbSet
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedByTokenHash, reason), cancellationToken);

        if (revoked > 0)
            logger.LogInformation("Revoked {Count} refresh token(s) for user {UserId} (reason: {Reason})", revoked, userId, reason);

        return revoked;
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        var deleted = await _dbSet
            .Where(t => t.ExpiresAt < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
            logger.LogInformation("Deleted {Count} expired refresh token(s) (cutoff {Cutoff:o})", deleted, cutoffUtc);

        return deleted;
    }
}
