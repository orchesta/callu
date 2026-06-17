using FluentValidation;
using Callu.Shared.Models.Escalations;

namespace Callu.Application.Validators;

public class CreateEscalationRequestValidator : AbstractValidator<CreateEscalationRequest>
{
    public CreateEscalationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Policy name is required")
            .MaximumLength(200).WithMessage("Policy name cannot exceed 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.TeamId)
            .NotEqual(Guid.Empty).WithMessage("Invalid team ID")
            .When(x => x.TeamId.HasValue);
    }
}
