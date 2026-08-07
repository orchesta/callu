using FluentValidation;
using Callu.Shared.Models.Teams;

namespace Callu.Application.Validators;

public class UpdateTeamRequestValidator : AbstractValidator<UpdateTeamRequest>
{
    public UpdateTeamRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100)
            .When(x => x.Name is not null);
    }
}
