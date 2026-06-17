using FluentValidation;
using Callu.Shared.Models.Settings;

namespace Callu.Application.Validators;

public class UpdateSmtpSettingsRequestValidator : AbstractValidator<UpdateSmtpSettingsRequest>
{
    public UpdateSmtpSettingsRequestValidator()
    {
        RuleFor(x => x.Host)
            .NotEmpty().WithMessage("SMTP host is required")
            .MaximumLength(255);

        RuleFor(x => x.Port)
            .InclusiveBetween(1, 65535).WithMessage("Port must be between 1 and 65535");

        RuleFor(x => x.FromAddress)
            .NotEmpty().WithMessage("From address is required")
            .EmailAddress().WithMessage("Invalid email format");
    }
}

public class SendTestEmailRequestValidator : AbstractValidator<SendTestEmailRequest>
{
    public SendTestEmailRequestValidator()
    {
        RuleFor(x => x.RecipientEmail)
            .NotEmpty().WithMessage("Recipient email is required")
            .EmailAddress().WithMessage("Invalid email format");
    }
}
