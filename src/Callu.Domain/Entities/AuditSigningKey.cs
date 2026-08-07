using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>An Ed25519 key pair used to sign exported audit events.</summary>
// Retired keys are kept: a signature made under a key that has since rotated still has to verify,
// and the public half is the only thing needed to check it.
public class AuditSigningKey : BaseEntity
{
    public const int MaxKeyIdLength = 64;

    /// <summary>Public identifier written into exported events; never key material.</summary>
    [StringLength(MaxKeyIdLength)]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Base64 of the raw 32-byte Ed25519 public key.</summary>
    [StringLength(128)]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>The private half, protected by the Data Protection key ring.</summary>
    [StringLength(2048)]
    public string ProtectedPrivateKey { get; set; } = string.Empty;

    /// <summary>Null while this is the key new exports are signed with.</summary>
    public DateTime? RetiredAt { get; set; }
}
