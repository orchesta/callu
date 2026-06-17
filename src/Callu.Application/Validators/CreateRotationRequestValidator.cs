using FluentValidation;
using Callu.Shared.Models.Schedules;

namespace Callu.Application.Validators;

public class CreateRotationRequestValidator : AbstractValidator<CreateRotationRequest>
{
    public CreateRotationRequestValidator()
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

        RuleFor(x => x.RecurrenceEndDate!.Value)
            .GreaterThanOrEqualTo(x => x.HandoverStartLocal.Date)
            .When(x => x.RecurrenceEndDate.HasValue)
            .WithMessage("RecurrenceEndDate must be on or after the rotation's start date.");
    }
}
