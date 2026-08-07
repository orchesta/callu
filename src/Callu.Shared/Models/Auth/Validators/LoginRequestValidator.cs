using FluentValidation;
using Callu.Shared.Validation;

namespace Callu.Shared.Models.Auth.Validators;

/// <summary>
/// Validator for LoginRequest
/// </summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .MaximumLength(IdentityFieldLengths.Email)
            .EmailAddress().WithMessage("Invalid email format");

        // Never stored: this bounds the hasher's input, not a column.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MaximumLength(PasswordRules.MaxLength);
    }
}
