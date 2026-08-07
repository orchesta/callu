using FluentValidation;
using Callu.Shared.Models.Auth;
using Callu.Shared.Validation;
using Callu.Api.Controllers;

namespace Callu.Api.Validators;

/// <summary>
/// Validates ForgotPasswordRequest - ensures Email is provided and valid
/// </summary>
public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(IdentityFieldLengths.Email)
            .EmailAddress().WithMessage("A valid email address is required.");
    }
}

/// <summary>
/// Validates ResetPasswordRequest - ensures Email, Token and NewPassword are provided
/// </summary>
public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(IdentityFieldLengths.Email)
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Reset token is required.")
            .MaximumLength(IdentityFieldLengths.Token);

        RuleFor(x => x.NewPassword).ApplyPasswordRules();
    }
}

public class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(IdentityFieldLengths.Email)
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Invitation token is required.")
            .MaximumLength(IdentityFieldLengths.Token);

        RuleFor(x => x.NewPassword).ApplyPasswordRules();
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        // Never stored: this bounds the hasher's input, not a column.
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.")
            .MaximumLength(PasswordRules.MaxLength);

        RuleFor(x => x.NewPassword).ApplyPasswordRules();
    }
}
