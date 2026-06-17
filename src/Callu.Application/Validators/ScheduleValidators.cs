using FluentValidation;
using Callu.Shared.Models.Schedules;

namespace Callu.Application.Validators;

public class CreateOverrideRequestValidator : AbstractValidator<CreateOverrideRequest>
{
    public CreateOverrideRequestValidator()
    {
        RuleFor(x => x.ScheduleId)
            .NotEmpty().WithMessage("Schedule ID is required.");

        RuleFor(x => x.OverrideUserId)
            .NotEmpty().WithMessage("Override user ID is required.");

        RuleFor(x => x.StartUtc)
            .NotEmpty().WithMessage("Start time is required.")
            .Must(BeUtcOrUnspecified).WithMessage("StartUtc must be a UTC timestamp.");

        RuleFor(x => x.EndUtc)
            .NotEmpty().WithMessage("End time is required.")
            .Must(BeUtcOrUnspecified).WithMessage("EndUtc must be a UTC timestamp.")
            .GreaterThan(x => x.StartUtc).WithMessage("End time must be after start time.");

        RuleFor(x => x.Reason)
            .MaximumLength(500).When(x => x.Reason is not null);
    }

    private static bool BeUtcOrUnspecified(DateTime dt) =>
        dt.Kind is DateTimeKind.Utc or DateTimeKind.Unspecified;
}

/// <summary>
/// PUT /api/schedules/overrides/{id} body validator. The endpoint accepts a
/// partial patch (all fields nullable) so each rule is gated on "field
/// supplied". A whole-record "EndUtc > StartUtc" rule fires only when both are
/// set to avoid two failure messages for one logical error.
/// </summary>
public class UpdateOverrideRequestValidator : AbstractValidator<UpdateOverrideRequest>
{
    public UpdateOverrideRequestValidator()
    {
        RuleFor(x => x.OverrideUserId!)
            .NotEmpty()
            .When(x => x.OverrideUserId is not null)
            .WithMessage("OverrideUserId must not be blank when supplied.");

        RuleFor(x => x.StartUtc!.Value)
            .Must(BeUtcOrUnspecified)
            .When(x => x.StartUtc.HasValue)
            .WithMessage("StartUtc must be a UTC timestamp.");

        RuleFor(x => x.EndUtc!.Value)
            .Must(BeUtcOrUnspecified)
            .When(x => x.EndUtc.HasValue)
            .WithMessage("EndUtc must be a UTC timestamp.");

        RuleFor(x => x)
            .Must(x => x.EndUtc!.Value > x.StartUtc!.Value)
            .When(x => x.StartUtc.HasValue && x.EndUtc.HasValue)
            .WithMessage("End time must be after start time.");

        RuleFor(x => x.Reason!)
            .MaximumLength(500)
            .When(x => x.Reason is not null);
    }

    private static bool BeUtcOrUnspecified(DateTime dt) =>
        dt.Kind is DateTimeKind.Utc or DateTimeKind.Unspecified;
}

public class UpdateRotationRequestValidator : AbstractValidator<UpdateRotationRequest>
{
    public UpdateRotationRequestValidator()
    {
        RuleFor(x => x.ShiftLengthMinutes)
            .GreaterThan(0)
            .LessThanOrEqualTo(60 * 24 * 30)
            .When(x => x.ShiftLengthMinutes.HasValue)
            .WithMessage("ShiftLengthMinutes must be positive and at most 30 days (43200 minutes).");

        RuleFor(x => x.Order)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Order.HasValue)
            .WithMessage("Order must be non-negative.");

        RuleFor(x => x.HandoverStartLocal!.Value.Year)
            .GreaterThan(1)
            .When(x => x.HandoverStartLocal.HasValue)
            .WithMessage("HandoverStartLocal must be a real date when supplied.");

        RuleFor(x => x.RecurrenceIntervalDays!.Value)
            .GreaterThan(0)
            .LessThanOrEqualTo(365)
            .When(x => x.RecurrenceIntervalDays.HasValue)
            .WithMessage("RecurrenceIntervalDays must be between 1 and 365 when supplied.");

        RuleFor(x => x)
            .Must(x => x.RecurrenceEndDate!.Value >= x.HandoverStartLocal!.Value.Date)
            .When(x => x.RecurrenceEndDate.HasValue && x.HandoverStartLocal.HasValue)
            .WithMessage("RecurrenceEndDate must be on or after the rotation's start date.");
    }
}
