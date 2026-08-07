using FluentValidation;
using Callu.Domain.Entities;
using Callu.Shared.Models.Escalations;

namespace Callu.Application.Validators;

public class UpdateEscalationRequestValidator : AbstractValidator<UpdateEscalationRequest>
{
    public UpdateEscalationRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100)
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

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
