using Callu.Domain.Enums;
using NodaTime;

namespace Callu.Shared.Models.Schedules;

/// <summary>Request to create a new rotation template, in the schedule's wall-clock timezone.</summary>
public record CreateRotationRequest
{
    public string UserId { get; init; } = string.Empty;
    public LocalDateTime HandoverStartLocal { get; init; }
    public int ShiftLengthMinutes { get; init; } = 1440;
    public bool IsPrimary { get; init; } = true;
    public int Order { get; init; } = 1;
    public RecurrenceType RecurrenceType { get; init; } = RecurrenceType.None;
    /// <summary>
    /// Exact period between handovers in days. Overrides <see cref="RecurrenceType"/> when set.
    /// Use when cadence doesn't fit the fixed enum (e.g. 2-day cycle for 2 members daily).
    /// </summary>
    public int? RecurrenceIntervalDays { get; init; }

    /// <summary>Consecutive calendar days this member owns from each handover; null means one occurrence per period.</summary>
    public int? OwnershipDays { get; init; }

    public LocalDate? RecurrenceEndDate { get; init; }
}
