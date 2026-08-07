using FluentValidation;
using Callu.Shared.Models.Maintenance;

namespace Callu.Application.Validators;

public class CreateMaintenanceWindowRequestValidator : AbstractValidator<CreateMaintenanceWindowRequest>
{
    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuppressAlerts",
        "AutoAcknowledge"
    };

    public CreateMaintenanceWindowRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .When(x => x.Description is not null);

        RuleFor(x => x.StartsAt)
            .NotEmpty().WithMessage("StartsAt is required");

        RuleFor(x => x.EndsAt)
            .NotEmpty().WithMessage("EndsAt is required")
            .GreaterThan(x => x.StartsAt).WithMessage("EndsAt must be after StartsAt")
            .Must(end => end > DateTime.UtcNow.AddMinutes(-1)).WithMessage("EndsAt must be in the future");

        RuleFor(x => x.Mode)
            .Must(m => string.IsNullOrEmpty(m) || AllowedModes.Contains(m))
            .WithMessage("Mode must be 'SuppressAlerts' or 'AutoAcknowledge'");

        RuleFor(x => x)
            .Must(req => req.AppliesToAllServices || req.AffectedServiceIds.Count > 0)
            .WithMessage("Specify at least one affected service or enable 'apply to all services'.");

        RuleForEach(x => x.AffectedServiceIds)
            .NotEqual(Guid.Empty).WithMessage("AffectedServiceIds must not contain empty GUIDs.");
    }
}

public class UpdateMaintenanceWindowRequestValidator : AbstractValidator<UpdateMaintenanceWindowRequest>
{
    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuppressAlerts",
        "AutoAcknowledge"
    };

    public UpdateMaintenanceWindowRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .When(x => x.Description is not null);

        RuleFor(x => x.StartsAt)
            .NotEmpty().WithMessage("StartsAt is required");

        RuleFor(x => x.EndsAt)
            .NotEmpty().WithMessage("EndsAt is required")
            .GreaterThan(x => x.StartsAt).WithMessage("EndsAt must be after StartsAt")
            .Must(end => end > DateTime.UtcNow.AddMinutes(-1)).WithMessage("EndsAt must be in the future");

        RuleFor(x => x.Mode)
            .Must(m => string.IsNullOrEmpty(m) || AllowedModes.Contains(m))
            .WithMessage("Mode must be 'SuppressAlerts' or 'AutoAcknowledge'");

        RuleFor(x => x)
            .Must(req => req.AppliesToAllServices || req.AffectedServiceIds.Count > 0)
            .WithMessage("Specify at least one affected service or enable 'apply to all services'.");

        RuleForEach(x => x.AffectedServiceIds)
            .NotEqual(Guid.Empty).WithMessage("AffectedServiceIds must not contain empty GUIDs.");
    }
}
