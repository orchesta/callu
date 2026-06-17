using FluentValidation;
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
    }
}
