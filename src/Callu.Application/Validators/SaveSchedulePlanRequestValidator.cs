using FluentValidation;
using NodaTime;
using Callu.Shared.Models.Schedules;

namespace Callu.Application.Validators;

/// <summary>Bounds for the batch plan endpoint; the per-rotation rules mirror
/// <see cref="CreateRotationRequestValidator"/> so the two paths cannot disagree.</summary>
public class SaveSchedulePlanRequestValidator : AbstractValidator<SaveSchedulePlanRequest>
{
    public SaveSchedulePlanRequestValidator(IDateTimeZoneProvider tzProvider)
    {
        RuleFor(x => x.Name!)
            .NotEmpty()
            .MaximumLength(100).WithMessage("Schedule name cannot exceed 100 characters")
            .When(x => x.Name is not null);

        RuleFor(x => x.Description!)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Timezone!)
            .Must(tz => !string.IsNullOrWhiteSpace(tz) && tzProvider.GetZoneOrNull(tz) != null)
            .WithMessage("Timezone must be a valid IANA identifier (e.g. 'Europe/Istanbul').")
            .When(x => x.Timezone is not null);

        RuleFor(x => x.TeamId!.Value)
            .NotEqual(Guid.Empty).WithMessage("Team is required")
            .When(x => x.TeamId.HasValue);

        RuleForEach(x => x.Rotations!)
            .SetValidator(new SchedulePlanRotationValidator())
            .When(x => x.Rotations is not null);

        // `rotations` is a full replacement, so an empty list would leave a live schedule with nobody on it.
        RuleFor(x => x.Rotations!)
            .Must(r => r.Count > 0)
            .WithMessage(
                "A schedule cannot be saved with an empty rotation list — it would leave nobody on call. "
                + "Omit 'rotations' to leave the stored rotations untouched, or delete the schedule.")
            .When(x => x.Rotations is not null);

        // Two rotations for one member would page them twice per cycle, and two entries for one
        // stored rotation would make the last one silently win.
        RuleFor(x => x.Rotations!)
            .Must(NoDuplicateUsers).WithMessage("A member can appear at most once in a schedule's rotations.")
            .Must(NoDuplicateIds).WithMessage("A rotation can appear at most once in the plan.")
            .When(x => x.Rotations is not null);
    }

    private static bool NoDuplicateUsers(IReadOnlyList<SchedulePlanRotation> rotations) =>
        rotations
            .Select(r => r.UserId)
            .Distinct(StringComparer.Ordinal)
            .Count() == rotations.Count;

    private static bool NoDuplicateIds(IReadOnlyList<SchedulePlanRotation> rotations)
    {
        var ids = rotations.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToList();
        return ids.Distinct().Count() == ids.Count;
    }
}

public class SchedulePlanRotationValidator : AbstractValidator<SchedulePlanRotation>
{
    public SchedulePlanRotationValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User is required");

        RuleFor(x => x.HandoverStartLocal.Year)
            .GreaterThan(1).WithMessage("HandoverStartLocal is required");

        RuleFor(x => x.ShiftLengthMinutes)
            .GreaterThan(0).WithMessage("ShiftLengthMinutes must be positive")
            .LessThanOrEqualTo(60 * 24 * 30).WithMessage("ShiftLengthMinutes cannot exceed 30 days");

        RuleFor(x => x.Order)
            .GreaterThanOrEqualTo(0).WithMessage("Order must be 0 or greater");

        RuleFor(x => x.RecurrenceIntervalDays!.Value)
            .GreaterThan(0)
            .LessThanOrEqualTo(365)
            .When(x => x.RecurrenceIntervalDays.HasValue)
            .WithMessage("RecurrenceIntervalDays must be between 1 and 365 when supplied.");

        RuleFor(x => x.OwnershipDays!.Value)
            .GreaterThan(0)
            .LessThanOrEqualTo(31)
            .When(x => x.OwnershipDays.HasValue)
            .WithMessage("OwnershipDays must be between 1 and 31 when supplied.");

        RuleFor(x => x.RecurrenceEndDate!.Value)
            .GreaterThanOrEqualTo(x => x.HandoverStartLocal.Date)
            .When(x => x.RecurrenceEndDate.HasValue)
            .WithMessage("RecurrenceEndDate must be on or after the rotation's start date.");
    }
}
