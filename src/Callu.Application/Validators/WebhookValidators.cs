using FluentValidation;
using Callu.Shared.Models.Webhooks;

namespace Callu.Application.Validators;


public class CreateWebhookTemplateRequestValidator : AbstractValidator<CreateWebhookTemplateRequest>
{
    public CreateWebhookTemplateRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Template name is required")
            .MaximumLength(100);
    }
}

public class UpdateWebhookTemplateRequestValidator : AbstractValidator<UpdateWebhookTemplateRequest>
{
    public UpdateWebhookTemplateRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100)
            .When(x => x.Name is not null);
    }
}
