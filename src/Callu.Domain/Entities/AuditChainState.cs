using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// The single row holding the audit chain's key, its tail, and how far retention has pruned it.
/// </summary>
public class AuditChainState : BaseEntity
{
    /// <summary>The only row's primary key, so "there is one row" is enforced by the database.</summary>
    public static readonly Guid SingletonId = new("a0d17e5c-3f8b-4f2e-9c31-6b0a2d5e7f10");

    /// <summary>The chain's HMAC key, protected by the Data Protection key ring.</summary>
    [StringLength(2048)]
    public string ProtectedKey { get; set; } = string.Empty;

    /// <summary>Sequence of the last sealed entry; 0 before anything is sealed.</summary>
    public long LastSequence { get; set; }

    /// <summary>Hash of the last sealed entry, and what the next one links back to.</summary>
    [StringLength(64)]
    public string? LastHash { get; set; }

    /// <summary>Highest sequence retention has deleted, so a pruned prefix is not read as a break.</summary>
    public long PrunedThroughSequence { get; set; }

    /// <summary>Hash of the last pruned entry, which the oldest surviving entry must still link to.</summary>
    [StringLength(64)]
    public string? PrunedThroughHash { get; set; }
}
