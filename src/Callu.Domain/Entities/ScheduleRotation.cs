using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;
using NodaTime;

namespace Callu.Domain.Entities;

/// <summary>Rotation template — wall-clock handover, shift length and recurrence, expanded into UTC occurrences.</summary>
public class ScheduleRotation : BaseEntity
{
    public Guid ScheduleId { get; set; }
    public virtual Schedule Schedule { get; set; } = null!;

    [Required]
    [StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>First handover, expressed in the schedule's timezone.</summary>
    public LocalDateTime HandoverStartLocal { get; set; }

    /// <summary>Wall-clock shift length. 1440 = 24 hours. On DST days the actual UTC duration can be ±1h.</summary>
    public int ShiftLengthMinutes { get; set; } = 1440;

    public bool IsPrimary { get; set; } = true;

    /// <summary>Tie-breaker between concurrent rotations.</summary>
    public int Order { get; set; } = 1;

    public RecurrenceType RecurrenceType { get; set; } = RecurrenceType.None;

    /// <summary>Exact period in days between this rotation's handovers; overrides the recurrence type when set.</summary>
    public int? RecurrenceIntervalDays { get; set; }

    /// <summary>Consecutive calendar days this member owns from each handover; null means one occurrence per period.</summary>
    public int? OwnershipDays { get; set; }

    /// <summary>Last local date at which a new occurrence may start. null = open-ended.</summary>
    public LocalDate? RecurrenceEndDate { get; set; }

    public virtual ICollection<ScheduleOccurrence> Occurrences { get; set; } = new List<ScheduleOccurrence>();
}
