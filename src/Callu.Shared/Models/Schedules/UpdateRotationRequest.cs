using Callu.Domain.Enums;
using NodaTime;

namespace Callu.Shared.Models.Schedules;

/// <summary>Partial patch: a null field leaves the stored value untouched, so a value can be changed but not cleared.</summary>
public record UpdateRotationRequest
{
    public LocalDateTime? HandoverStartLocal { get; init; }
    public int? ShiftLengthMinutes { get; init; }
    public bool? IsPrimary { get; init; }
    public int? Order { get; init; }
    public RecurrenceType? RecurrenceType { get; init; }
    public int? RecurrenceIntervalDays { get; init; }
    public int? OwnershipDays { get; init; }
    public LocalDate? RecurrenceEndDate { get; init; }
}
