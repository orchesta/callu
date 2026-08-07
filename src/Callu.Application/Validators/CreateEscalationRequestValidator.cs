using FluentValidation;
using Callu.Domain.Entities;
using Callu.Shared.Models.Escalations;

namespace Callu.Application.Validators;

public class CreateEscalationRequestValidator : AbstractValidator<CreateEscalationRequest>
{
    public CreateEscalationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Policy name is required")
            .MaximumLength(100).WithMessage("Policy name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.TeamId)
            .NotEqual(Guid.Empty).WithMessage("Invalid team ID")
            .When(x => x.TeamId.HasValue);

        RuleFor(x => x.ExhaustionBehavior)
            .IsInEnum()
            .When(x => x.ExhaustionBehavior.HasValue);

        RuleFor(x => x.MaxRepeatCycles)
            .InclusiveBetween(EscalationPolicy.MinRepeatCycles, EscalationPolicy.MaxRepeatCyclesCap)
            .When(x => x.MaxRepeatCycles.HasValue);

        RuleFor(x => x.MaxRepeatDurationMinutes)
            .InclusiveBetween(EscalationPolicy.MinRepeatDurationMinutes, EscalationPolicy.MaxRepeatDurationMinutesCap)
            .When(x => x.MaxRepeatDurationMinutes.HasValue);
    }
}
