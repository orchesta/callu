using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>A public-chain head and its tree root, as OpenAuditModel checkpoint and proof-root documents.</summary>
public class AuditCheckpoint : BaseEntity
{
    [StringLength(64)] public string ChainId { get; set; } = string.Empty;
    public long HeadSequence { get; set; }
    [StringLength(64)] public string HeadHash { get; set; } = string.Empty;
    public long EventCount { get; set; }
    [StringLength(64)] public string RootHash { get; set; } = string.Empty;
    [StringLength(64)] public string AnchorType { get; set; } = string.Empty;
    [StringLength(2048)] public string AnchorReference { get; set; } = string.Empty;

    /// <summary>The signed checkpoint document, canonical text.</summary>
    public string Document { get; set; } = string.Empty;

    /// <summary>The signed proof root object, canonical text; every proof cut from this tree carries it.</summary>
    public string RootDocument { get; set; } = string.Empty;

    public AuditCheckpointStatus Status { get; set; }
    public DateTime? IssuedAt { get; set; }
    public int PublishAttempts { get; set; }
    [StringLength(500)] public string? LastError { get; set; }
}
