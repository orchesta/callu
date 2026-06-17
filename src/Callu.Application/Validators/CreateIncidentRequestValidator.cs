using FluentValidation;
using Callu.Shared.Models.Incidents;

namespace Callu.Application.Validators;

public class CreateIncidentRequestValidator : AbstractValidator<CreateIncidentRequest>
{
    public CreateIncidentRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Incident title is required")
            .MaximumLength(200).WithMessage("Incident title cannot exceed 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Severity)
            .Must(s => new[] { "Critical", "High", "Medium", "Low" }.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Invalid severity level. Valid values: Critical, High, Medium, Low");

        RuleFor(x => x.ServiceId)
            .NotEqual(Guid.Empty).WithMessage("Invalid service ID")
            .When(x => x.ServiceId.HasValue);
    }
}
