using FluentValidation;
using NodaTime;
using Callu.Shared.Models.Schedules;

namespace Callu.Application.Validators;

public class UpdateScheduleRequestValidator : AbstractValidator<UpdateScheduleRequest>
{
    public UpdateScheduleRequestValidator(IDateTimeZoneProvider tzProvider)
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Schedule name cannot exceed 100 characters")
            .MinimumLength(2).WithMessage("Schedule name must be at least 2 characters")
            .When(x => !string.IsNullOrEmpty(x.Name));

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Timezone)
            .Must(tz => !string.IsNullOrWhiteSpace(tz) && tzProvider.GetZoneOrNull(tz) != null)
            .WithMessage("Timezone must be a valid IANA identifier (e.g. 'Europe/Istanbul').")
            .When(x => !string.IsNullOrEmpty(x.Timezone));
    }
}
