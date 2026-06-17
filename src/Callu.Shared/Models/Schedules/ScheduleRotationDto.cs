using Callu.Domain.Enums;
using NodaTime;

namespace Callu.Shared.Models.Schedules;

/// <summary>
/// Rotation projection. Two shapes share this record:
///   - Rotation template (what's stored): HandoverStartLocal + ShiftLengthMinutes + Recurrence.
///   - Materialized occurrence (what runs): StartUtc + EndUtc on a specific day.
/// Consumers pick the fields they need. Occurrence-specific fields are nullable on the
/// template projection, and template-specific fields are nullable on occurrences.
/// </summary>
public record ScheduleRotationDto
{
    public Guid Id { get; init; }
    public Guid ScheduleId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string? UserName { get; init; }
    public string? UserInitials { get; init; }
    public bool IsPrimary { get; init; }
    public int Order { get; init; }

    public LocalDateTime? HandoverStartLocal { get; init; }
    public int ShiftLengthMinutes { get; init; }
    public RecurrenceType? RecurrenceType { get; init; }
    public int? RecurrenceIntervalDays { get; init; }
    public LocalDate? RecurrenceEndDate { get; init; }

    public DateTime? StartUtc { get; init; }
    public DateTime? EndUtc { get; init; }
}
