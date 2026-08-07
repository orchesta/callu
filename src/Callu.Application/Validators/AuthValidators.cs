using FluentValidation;
using Callu.Shared.Models.Auth;
using Callu.Shared.Localization;
using Callu.Shared.Validation;

namespace Callu.Application.Validators;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .MaximumLength(IdentityFieldLengths.FirstName)
            .When(x => x.FirstName is not null);

        RuleFor(x => x.LastName)
            .MaximumLength(IdentityFieldLengths.LastName)
            .When(x => x.LastName is not null);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(IdentityFieldLengths.PhoneNumber)
            .When(x => x.PhoneNumber is not null);

        RuleFor(x => x.Timezone)
            .MaximumLength(IdentityFieldLengths.Timezone)
            .When(x => x.Timezone is not null);

        // Shape, not membership: an operator adds a language by saving a TTS template, so a fixed
        // allowlist here would lock them out of one they can genuinely speak.
        RuleFor(x => x.Culture)
            .MaximumLength(IdentityFieldLengths.Culture)
            .Matches("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")
            .WithMessage("Culture must be a language tag such as 'tr-TR'.")
            .When(x => !string.IsNullOrEmpty(x.Culture));
    }
}

public class AdminUpdateUserRequestValidator : AbstractValidator<AdminUpdateUserRequest>
{
    public AdminUpdateUserRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .MaximumLength(IdentityFieldLengths.FirstName)
            .When(x => x.FirstName is not null);

        RuleFor(x => x.LastName)
            .MaximumLength(IdentityFieldLengths.LastName)
            .When(x => x.LastName is not null);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(IdentityFieldLengths.PhoneNumber)
            .When(x => x.PhoneNumber is not null);
    }
}

public class InviteUserRequestValidator : AbstractValidator<InviteUserRequest>
{
    public InviteUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(IdentityFieldLengths.Email)
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .MaximumLength(IdentityFieldLengths.RoleName);
    }
}

public class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .MaximumLength(IdentityFieldLengths.RoleName);
    }
}
