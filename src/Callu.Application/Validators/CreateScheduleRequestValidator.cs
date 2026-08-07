using FluentValidation;
using NodaTime;
using Callu.Shared.Models.Schedules;

namespace Callu.Application.Validators;

public class CreateScheduleRequestValidator : AbstractValidator<CreateScheduleRequest>
{
    private readonly IDateTimeZoneProvider _tzProvider;

    public CreateScheduleRequestValidator(IDateTimeZoneProvider tzProvider)
    {
        _tzProvider = tzProvider;

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Schedule name is required")
            .MaximumLength(100).WithMessage("Schedule name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.TeamId)
            .NotEqual(Guid.Empty).WithMessage("Team is required");

        RuleFor(x => x.Timezone)
            .NotEmpty().WithMessage("Timezone is required")
            .Must(BeValidIanaTimezone).WithMessage("Timezone must be a valid IANA identifier (e.g. 'Europe/Istanbul').");
    }

    private bool BeValidIanaTimezone(string timezone) =>
        !string.IsNullOrWhiteSpace(timezone) && _tzProvider.GetZoneOrNull(timezone) != null;
}
