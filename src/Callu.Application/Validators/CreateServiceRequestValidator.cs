using FluentValidation;
using Callu.Domain.Enums;
using Callu.Shared.Models.Services;

namespace Callu.Application.Validators;

public class CreateServiceRequestValidator : AbstractValidator<CreateServiceRequest>
{
    public CreateServiceRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Service name is required")
            .MaximumLength(100).WithMessage("Service name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Service type is required")
            .IsEnumName(typeof(ServiceType), caseSensitive: false)
            .WithMessage("Invalid service type");

        RuleFor(x => x.TeamId)
            .NotEqual(Guid.Empty).WithMessage("Invalid team ID")
            .When(x => x.TeamId.HasValue);
    }
}
