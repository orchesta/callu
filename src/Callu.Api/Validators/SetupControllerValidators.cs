using FluentValidation;
using Callu.Domain.Entities;
using Callu.Shared.Models.Settings;
using Callu.Shared.Validation;

namespace Callu.Api.Validators;

/// <summary>
/// Validates InitialSetupRequest - ensures required fields for first-time setup
/// </summary>
public class InitialSetupRequestValidator : AbstractValidator<InitialSetupRequest>
{
    public InitialSetupRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(IdentityFieldLengths.Email)
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Name)
            .MaximumLength(IdentityFieldLengths.DisplayName)
            .When(x => x.Name is not null);

        // The narrower of the two columns it lands in; ApplicationUser.Timezone is varchar(100).
        RuleFor(x => x.DefaultTimezone)
            .MaximumLength(OrganizationSettings.MaxDefaultTimezoneLength)
            .When(x => x.DefaultTimezone is not null);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^A-Za-z0-9]").WithMessage("Password must contain at least one special character.")
            .Must(pw => pw is null || pw.Distinct().Count() >= 4)
                .WithMessage("Password must contain at least 4 distinct characters.");
    }
}
