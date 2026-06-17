using Callu.Domain.Enums;
using NodaTime;

namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Request to create a new rotation template.
///
/// Time model:
///   - <see cref="HandoverStartLocal"/> is a wall-clock <c>LocalDateTime</c> in the schedule's
///     IANA timezone. Frontends send ISO-8601 without offset (e.g. <c>"2026-04-20T09:00:00"</c>).
///   - <see cref="ShiftLengthMinutes"/> is the wall-clock duration of one shift.
///   - <see cref="RecurrenceType"/> controls how often a new handover starts.
///   - <see cref="RecurrenceEndDate"/> (optional) caps recurrence as a local date — rotations
///     past that date are not materialized.
/// </summary>
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
    public LocalDate? RecurrenceEndDate { get; init; }
}
