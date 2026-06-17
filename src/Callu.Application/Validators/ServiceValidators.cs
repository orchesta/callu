using FluentValidation;
using Callu.Shared.Models.Services;

namespace Callu.Application.Validators;

public class UpdateServiceRequestValidator : AbstractValidator<UpdateServiceRequest>
{
    public UpdateServiceRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100)
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);
    }
}

public class CreateServiceDependencyRequestValidator : AbstractValidator<CreateServiceDependencyRequest>
{
    public CreateServiceDependencyRequestValidator()
    {
        RuleFor(x => x.DependsOnServiceId)
            .NotEmpty().WithMessage("Dependency service ID is required");
    }
}
