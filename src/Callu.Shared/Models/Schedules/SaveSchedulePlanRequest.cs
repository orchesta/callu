using Callu.Domain.Enums;
using NodaTime;

namespace Callu.Shared.Models.Schedules;

/// <summary>The whole on-call plan for one schedule, applied as one transaction and one rematerialize.</summary>
/// <remarks>Schedule fields are a patch; <see cref="Rotations"/> is a full replacement, and an empty list is rejected.</remarks>
public record SaveSchedulePlanRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Timezone { get; init; }
    public Guid? TeamId { get; init; }

    /// <summary>
    /// Desired rotations in full. null = leave the stored rotations as they are. An empty list is
    /// not "leave them alone" and is not accepted — see the type's remarks.
    /// </summary>
    public IReadOnlyList<SchedulePlanRotation>? Rotations { get; init; }
}

/// <summary>
/// One rotation of a <see cref="SaveSchedulePlanRequest"/>. Unlike <see cref="UpdateRotationRequest"/>
/// this is not a patch: every field is written as given, so a value can also be cleared.
/// </summary>
public record SchedulePlanRotation
{
    /// <summary>Existing rotation to update. null creates a new one.</summary>
    public Guid? Id { get; init; }

    public string UserId { get; init; } = string.Empty;
    public LocalDateTime HandoverStartLocal { get; init; }
    public int ShiftLengthMinutes { get; init; } = 1440;
    public bool IsPrimary { get; init; }
    public int Order { get; init; } = 1;
    public RecurrenceType RecurrenceType { get; init; } = RecurrenceType.None;
    public int? RecurrenceIntervalDays { get; init; }
    public int? OwnershipDays { get; init; }
    public LocalDate? RecurrenceEndDate { get; init; }
}
