using FluentValidation;
using Callu.Shared.Models.Communication;

namespace Callu.Application.Validators;

public class CreateProviderRequestValidator : AbstractValidator<CreateProviderRequest>
{
    public CreateProviderRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Provider name is required")
            .MaximumLength(100);

        RuleFor(x => x.ProviderType)
            .NotEmpty().WithMessage("Provider type is required")
            .MaximumLength(50);
    }
}

public class UpdateProviderRequestValidator : AbstractValidator<UpdateProviderRequest>
{
    public UpdateProviderRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Provider name is required")
            .MaximumLength(100);
    }
}

public class CreateSipTrunkRequestValidator : AbstractValidator<CreateSipTrunkRequest>
{
    public CreateSipTrunkRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100);

        RuleFor(x => x.Server)
            .NotEmpty().WithMessage("Server is required")
            .MaximumLength(255);

        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .MaximumLength(100);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");

        RuleFor(x => x.Port)
            .InclusiveBetween(1, 65535).WithMessage("Port must be between 1 and 65535");
    }
}

public class UpdateSipTrunkRequestValidator : AbstractValidator<UpdateSipTrunkRequest>
{
    public UpdateSipTrunkRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100);

        RuleFor(x => x.Server)
            .NotEmpty().WithMessage("Server is required")
            .MaximumLength(255);

        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .MaximumLength(100);

        RuleFor(x => x.Port)
            .InclusiveBetween(1, 65535).WithMessage("Port must be between 1 and 65535");
    }
}

public class TtsTemplateSaveRequestValidator : AbstractValidator<TtsTemplateSaveRequest>
{
    public TtsTemplateSaveRequestValidator()
    {
        RuleFor(x => x.LanguageCode)
            .NotEmpty().WithMessage("Language code is required")
            .MaximumLength(10);

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MaximumLength(100);
    }
}

public class MakeCallRequestValidator : AbstractValidator<MakeCallRequest>
{
    public MakeCallRequestValidator()
    {
        RuleFor(x => x.Destination)
            .NotEmpty().WithMessage("Destination is required");
    }
}

public class SendSmsRequestValidator : AbstractValidator<SendSmsRequest>
{
    public SendSmsRequestValidator()
    {
        RuleFor(x => x.To)
            .NotEmpty().WithMessage("Recipient number is required");

        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message is required")
            .MaximumLength(1600);
    }
}

