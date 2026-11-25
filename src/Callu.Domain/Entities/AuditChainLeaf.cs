using System.ComponentModel.DataAnnotations;

namespace Callu.Domain.Entities;

/// <summary>One public-chain digest, kept for the Merkle trees after retention deletes its row.</summary>
// A checkpoint taken before a prune must still yield proofs after it, and its tree runs from the
// chain's first event. About 100 bytes a row with tuple overhead; never pruned.
public class AuditChainLeaf
{
    [Key]
    public long Sequence { get; set; }

    [StringLength(64)]
    public string Hash { get; set; } = string.Empty;
}
