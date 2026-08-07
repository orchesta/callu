namespace Callu.Shared.Models.Audit;

/// <summary>A published Ed25519 public key. Never carries the private half.</summary>
public sealed record AuditSigningPublicKeyDto
{
    public string KeyId { get; init; } = string.Empty;
    public string Algorithm { get; init; } = "Ed25519";
    /// <summary>Base64 of the raw 32-byte public key.</summary>
    public string PublicKey { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    /// <summary>Null for the key new exports are signed with.</summary>
    public DateTime? RetiredAt { get; init; }
}
