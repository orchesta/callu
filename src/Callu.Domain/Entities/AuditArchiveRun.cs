using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// One archived day of the audit trail, and the manifest entry that describes it.
/// </summary>
public class AuditArchiveRun : BaseEntity
{
    /// <summary>The UTC day this file covers; one run per day.</summary>
    public DateOnly Day { get; set; }

    [StringLength(260)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>SHA-256 of the compressed file, base64.</summary>
    [StringLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    public int RowCount { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>First and last chain sequence in the file; null if nothing in it was sealed.</summary>
    public long? FirstSequence { get; set; }

    public long? LastSequence { get; set; }

    public DateTime FirstCreatedAt { get; set; }

    public DateTime LastCreatedAt { get; set; }
}
