using FluentValidation;
using Callu.Domain.Entities;
using Callu.Shared.Models.Devices;
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

public class UpdateFirebaseSettingsRequestValidator : AbstractValidator<UpdateFirebaseSettingsRequest>
{
    public UpdateFirebaseSettingsRequestValidator()
    {
        RuleFor(x => x.ProjectId)
            .MaximumLength(FirebaseSettings.MaxProjectIdLength)
            .Must(p => FirebaseSettings.IsValidProjectId(p!.Trim()))
            .WithMessage("Project id may contain only lowercase letters, digits, and hyphens")
            .When(x => !string.IsNullOrEmpty(x.ProjectId));

        RuleFor(x => x.ServiceAccountJson)
            .MaximumLength(FirebaseSettings.MaxCredentialLength)
            .When(x => !string.IsNullOrEmpty(x.ServiceAccountJson));
    }
}

public class RegisterPushDeviceRequestValidator : AbstractValidator<RegisterPushDeviceRequest>
{
    public RegisterPushDeviceRequestValidator()
    {
        RuleFor(x => x.Platform)
            .NotEmpty()
            .MaximumLength(PushDeviceLimits.MaxPlatformLength)
            .Must(p => PushPlatforms.Allowed.Contains(PushPlatforms.Normalize(p)))
            .WithMessage("Platform must be ios, android, or web");

        RuleFor(x => x.PushToken)
            .NotEmpty()
            .MaximumLength(PushDeviceLimits.MaxPushTokenLength);
    }
}

public class UnregisterPushDeviceRequestValidator : AbstractValidator<UnregisterPushDeviceRequest>
{
    public UnregisterPushDeviceRequestValidator()
    {
        RuleFor(x => x.PushToken)
            .MaximumLength(PushDeviceLimits.MaxPushTokenLength)
            .When(x => !string.IsNullOrEmpty(x.PushToken));
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
