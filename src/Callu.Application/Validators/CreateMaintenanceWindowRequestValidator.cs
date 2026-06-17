using FluentValidation;
using Callu.Shared.Models.Maintenance;

namespace Callu.Application.Validators;

/// <summary>
/// Validates maintenance window create requests. Fix 11.G11 — the create path used
/// to accept <c>StartsAt &gt; EndsAt</c>, end-in-the-past windows, and free-form
/// mode strings (which then silently fell back to <c>SuppressAlerts</c>).
/// </summary>
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
