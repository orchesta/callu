using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using NodaTime;

namespace Callu.Domain.Entities;

/// <summary>
/// Manual override for an on-call schedule. Times are absolute UTC instants — the operator
/// picks a real-world moment and the UI converts local input to UTC at write time.
/// </summary>
public class OnCallOverride : BaseEntity
{
    public Guid ScheduleId { get; set; }
    public virtual Schedule Schedule { get; set; } = null!;

    [Required]
    [StringLength(128)]
    public string OverrideUserId { get; set; } = string.Empty;

    [StringLength(128)]
    public string? OriginalUserId { get; set; }

    public Instant StartUtc { get; set; }
    /// <summary>Exclusive end.</summary>
    public Instant EndUtc { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }

    /// <summary>Soft on/off toggle, so an override can be retired without deleting it.</summary>
    public bool IsActive { get; set; } = true;
}
