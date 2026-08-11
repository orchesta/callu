using FluentValidation;
using Callu.Domain.Entities;
using Callu.Shared.Models.Incidents;

namespace Callu.Application.Validators;

public class CreateIncidentRequestValidator : AbstractValidator<CreateIncidentRequest>
{
    public CreateIncidentRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Incident title is required")
            .MaximumLength(Incident.MaxTitleLength)
            .WithMessage($"Incident title cannot exceed {Incident.MaxTitleLength} characters");

        RuleFor(x => x.Description)
            .MaximumLength(Incident.MaxDescriptionLength)
            .WithMessage($"Description cannot exceed {Incident.MaxDescriptionLength} characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Severity)
            .Must(s => new[] { "Critical", "High", "Medium", "Low" }.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Invalid severity level. Valid values: Critical, High, Medium, Low");

        RuleFor(x => x.ServiceId)
            .NotEqual(Guid.Empty).WithMessage("Invalid service ID")
            .When(x => x.ServiceId.HasValue);
    }
}
