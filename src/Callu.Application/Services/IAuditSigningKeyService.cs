using Callu.Shared.Models.Audit;

namespace Callu.Application.Services;

/// <summary>Custody of the Ed25519 keys exported audit events are signed with.</summary>
public interface IAuditSigningKeyService
{
    /// <summary>The signer for new exports, or null when signing is off or the key cannot be read.</summary>
    Task<IOpenAuditSigner?> GetActiveSignerAsync(CancellationToken cancellationToken = default);

    /// <summary>Every public key, retired ones included, so older exports stay checkable.</summary>
    Task<IReadOnlyList<AuditSigningPublicKeyDto>> GetPublicKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Retires the current key and starts signing with a new one.</summary>
    Task<AuditSigningPublicKeyDto> RotateAsync(CancellationToken cancellationToken = default);
}
