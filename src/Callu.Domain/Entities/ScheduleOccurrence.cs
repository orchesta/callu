using Callu.Domain.Base;
using NodaTime;

namespace Callu.Domain.Entities;

/// <summary>
/// Materialized on-call slot — one concrete UTC interval for one user. Populated by
/// ScheduleMaterializer. On-call lookups are range scans against this table.
/// </summary>
public class ScheduleOccurrence : BaseEntity
{
    public Guid ScheduleId { get; set; }
    public virtual Schedule Schedule { get; set; } = null!;

    public Guid RotationId { get; set; }
    public virtual ScheduleRotation Rotation { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;

    public Instant StartUtc { get; set; }
    /// <summary>Exclusive end.</summary>
    public Instant EndUtc { get; set; }

    /// <summary>Cached from the parent rotation so range scans don't need a join.</summary>
    public bool IsPrimary { get; set; }
    public int Order { get; set; }

    public Instant MaterializedAt { get; set; }
}
