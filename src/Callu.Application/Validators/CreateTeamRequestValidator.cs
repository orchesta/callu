using FluentValidation;
using Callu.Shared.Models.Teams;

namespace Callu.Application.Validators;

public class CreateTeamRequestValidator : AbstractValidator<CreateTeamRequest>
{
    public CreateTeamRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Team name is required")
            .MinimumLength(2).WithMessage("Team name must be at least 2 characters")
            .MaximumLength(100).WithMessage("Team name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Icon)
            .MaximumLength(10).WithMessage("Icon cannot exceed 10 characters")
            .When(x => !string.IsNullOrEmpty(x.Icon));

        RuleFor(x => x.Color)
            .Matches("^(#[0-9a-fA-F]{6}|#[0-9a-fA-F]{3}|bg-[a-z]+-[0-9]+)$")
            .WithMessage("Color must be a hex code (e.g. #3B82F6) or one of the supported palette names.")
            .When(x => !string.IsNullOrEmpty(x.Color));
    }
}
