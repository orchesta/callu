using Callu.Domain.Enums;
using NodaTime;

namespace Callu.Shared.Models.Schedules;

public record UpdateRotationRequest
{
    public LocalDateTime? HandoverStartLocal { get; init; }
    public int? ShiftLengthMinutes { get; init; }
    public bool? IsPrimary { get; init; }
    public int? Order { get; init; }
    public RecurrenceType? RecurrenceType { get; init; }
    public int? RecurrenceIntervalDays { get; init; }
    public LocalDate? RecurrenceEndDate { get; init; }
}
