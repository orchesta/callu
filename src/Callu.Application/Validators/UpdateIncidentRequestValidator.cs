using FluentValidation;
using Callu.Shared.Models.Incidents;

namespace Callu.Application.Validators;

public class UpdateIncidentRequestValidator : AbstractValidator<UpdateIncidentRequest>
{
    public UpdateIncidentRequestValidator()
    {
        RuleFor(x => x.Title)
            .MaximumLength(200).WithMessage("Incident title cannot exceed 200 characters")
            .When(x => !string.IsNullOrEmpty(x.Title));

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Severity)
            .Must(s => new[] { "Critical", "High", "Medium", "Low" }.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Invalid severity level. Valid values: Critical, High, Medium, Low")
            .When(x => !string.IsNullOrEmpty(x.Severity));

        RuleFor(x => x.Status)
            .Must(s => new[] { "Open", "Acknowledged", "Investigating", "Mitigated", "Resolved", "Closed" }.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Invalid status. Valid values: Open, Acknowledged, Investigating, Mitigated, Resolved, Closed")
            .When(x => !string.IsNullOrEmpty(x.Status));

        RuleFor(x => x.ServiceId)
            .NotEqual(Guid.Empty).WithMessage("Invalid service ID")
            .When(x => x.ServiceId.HasValue);

        RuleFor(x => x.TeamId)
            .NotEqual(Guid.Empty).WithMessage("Invalid team ID")
            .When(x => x.TeamId.HasValue);
    }
}
