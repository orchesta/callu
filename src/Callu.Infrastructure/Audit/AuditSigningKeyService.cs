using System.Globalization;
using System.Security.Cryptography;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Parameters;

namespace Callu.Infrastructure.Audit;

/// <summary>Creates, protects and hands out the audit-export signing keys.</summary>
public class AuditSigningKeyService(
    ApplicationDbContext db,
    IDataProtectionProvider dataProtection,
    IOptions<AuditSigningOptions> options,
    ILogger<AuditSigningKeyService> logger) : IAuditSigningKeyService
{
    private const string ProtectorPurpose = "Callu.AuditSigning.v1";

    public async Task<IOpenAuditSigner?> GetActiveSignerAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return null;

        var key = await LoadOrCreateActiveKeyAsync(cancellationToken);

        try
        {
            var privateKey = Convert.FromBase64String(Protector().Unprotect(key.ProtectedPrivateKey));
            return new Ed25519Signer(key.KeyId, privateKey);
        }
        catch (CryptographicException ex)
        {
            // Exports keep working unsigned rather than failing: an operator who cannot read the
            // audit trail at all is worse off than one reading it without a signature.
            logger.LogError(ex,
                "Audit signing key {KeyId} cannot be decrypted with the current Data Protection key ring; "
                + "exports will be unsigned until the ring is restored.", key.KeyId);
            return null;
        }
    }

    public async Task<IReadOnlyList<AuditSigningPublicKeyDto>> GetPublicKeysAsync(
        CancellationToken cancellationToken = default) =>
        await db.AuditSigningKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new AuditSigningPublicKeyDto
            {
                KeyId = k.KeyId,
                Algorithm = Ed25519Signer.AlgorithmName,
                PublicKey = k.PublicKey,
                CreatedAt = k.CreatedAt,
                RetiredAt = k.RetiredAt,
            })
            .ToListAsync(cancellationToken);

    public async Task<AuditSigningPublicKeyDto> RotateAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Retired, never deleted: everything already exported was signed with it and still has to verify.
        await db.AuditSigningKeys
            .Where(k => k.RetiredAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RetiredAt, now), cancellationToken);

        var created = await CreateKeyAsync(cancellationToken);

        return new AuditSigningPublicKeyDto
        {
            KeyId = created.KeyId,
            Algorithm = Ed25519Signer.AlgorithmName,
            PublicKey = created.PublicKey,
            CreatedAt = created.CreatedAt,
        };
    }

    private async Task<AuditSigningKey> LoadOrCreateActiveKeyAsync(CancellationToken cancellationToken)
    {
        var existing = await db.AuditSigningKeys
            .AsNoTracking()
            .Where(k => k.RetiredAt == null)
            .OrderBy(k => k.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return existing ?? await CreateKeyAsync(cancellationToken);
    }

    private async Task<AuditSigningKey> CreateKeyAsync(CancellationToken cancellationToken)
    {
        var seed = RandomNumberGenerator.GetBytes(Ed25519PrivateKeyParameters.KeySize);
        var privateKey = new Ed25519PrivateKeyParameters(seed);
        var now = DateTime.UtcNow;

        var key = new AuditSigningKey
        {
            Id = Guid.NewGuid(),
            KeyId = $"callu-audit-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-"
                + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)),
            PublicKey = Convert.ToBase64String(privateKey.GeneratePublicKey().GetEncoded()),
            ProtectedPrivateKey = Protector().Protect(Convert.ToBase64String(seed)),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.AuditSigningKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Minted audit signing key {KeyId}.", key.KeyId);
        return key;
    }

    private IDataProtector Protector() => dataProtection.CreateProtector(ProtectorPurpose);
}
