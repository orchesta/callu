using Callu.Domain.Enums;
using NodaTime;

namespace Callu.Shared.Models.Schedules;

/// <summary>Rotation projection, shared by the stored template and a materialized occurrence.</summary>
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
    public int? OwnershipDays { get; init; }
    public LocalDate? RecurrenceEndDate { get; init; }

    public DateTime? StartUtc { get; init; }
    public DateTime? EndUtc { get; init; }
}
