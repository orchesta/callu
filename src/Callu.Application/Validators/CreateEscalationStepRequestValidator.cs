using FluentValidation;
using Callu.Shared.Models.Escalations;

namespace Callu.Application.Validators;

public class CreateEscalationStepRequestValidator : AbstractValidator<CreateEscalationStepRequest>
{
    public CreateEscalationStepRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Step title is required")
            .MaximumLength(200).WithMessage("Step title cannot exceed 200 characters");

        RuleFor(x => x.DelayMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Delay must be 0 or greater");

        RuleFor(x => x)
            .Must(x => (x.NotifyUserIds != null && x.NotifyUserIds.Any())
                       || x.ScheduleId.HasValue
                       || x.TeamId.HasValue)
            .WithMessage("Select users, a schedule, or a team for notifications")
            .WithName("NotificationTarget");

        RuleFor(x => x.NotifyUserIds)
            .Must(ids => ids == null || ids.All(id => !string.IsNullOrWhiteSpace(id)))
            .WithMessage("All user IDs must be valid")
            .When(x => x.NotifyUserIds != null);
    }
}
