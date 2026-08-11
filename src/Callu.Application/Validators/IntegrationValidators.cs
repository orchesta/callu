using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Integrations;
using FluentValidation;

namespace Callu.Application.Validators;

public class CreateIntegrationRequestValidator : AbstractValidator<CreateIntegrationRequest>
{
    public CreateIntegrationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Integration name is required")
            .MaximumLength(Integration.MaxNameLength)
            .WithMessage($"Integration name cannot exceed {Integration.MaxNameLength} characters");

        RuleFor(x => x.Description)
            .MaximumLength(Integration.MaxDescriptionLength)
            .WithMessage($"Description cannot exceed {Integration.MaxDescriptionLength} characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Integration type is required")
            .IsEnumName(typeof(IntegrationType), caseSensitive: false)
            .WithMessage("Invalid integration type");

        RuleFor(x => x.ServiceId)
            .NotEqual(Guid.Empty).WithMessage("Invalid service ID")
            .When(x => x.ServiceId.HasValue);

        RuleFor(x => x.TeamId)
            .NotEqual(Guid.Empty).WithMessage("Invalid team ID")
            .When(x => x.TeamId.HasValue);

        RuleFor(x => x.WebhookSecret)
            .MaximumLength(Integration.MaxWebhookSecretLength)
            .WithMessage($"Webhook secret cannot exceed {Integration.MaxWebhookSecretLength} characters")
            .When(x => !string.IsNullOrEmpty(x.WebhookSecret));

        RuleFor(x => x.WebhookSignatureHeader)
            .MaximumLength(Integration.MaxWebhookSignatureHeaderLength)
            .WithMessage($"Signature header cannot exceed {Integration.MaxWebhookSignatureHeaderLength} characters")
            .When(x => !string.IsNullOrEmpty(x.WebhookSignatureHeader));

        RuleFor(x => x.WebhookSignatureHeader)
            .NotEmpty().WithMessage("A signature header name is required when a secret is set")
            .When(x => !string.IsNullOrEmpty(x.WebhookSecret));
    }
}

public class BindIntegrationServiceRequestValidator : AbstractValidator<BindIntegrationServiceRequest>
{
    public BindIntegrationServiceRequestValidator()
    {
        RuleFor(x => x.ServiceId)
            .NotEqual(Guid.Empty).WithMessage("Invalid service ID")
            .When(x => x.ServiceId.HasValue);
    }
}

public class UpdateIntegrationRequestValidator : AbstractValidator<UpdateIntegrationRequest>
{
    public UpdateIntegrationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Integration name is required")
            .MaximumLength(Integration.MaxNameLength)
            .WithMessage($"Integration name cannot exceed {Integration.MaxNameLength} characters");

        RuleFor(x => x.Description)
            .MaximumLength(Integration.MaxDescriptionLength)
            .WithMessage($"Description cannot exceed {Integration.MaxDescriptionLength} characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.TeamId)
            .NotEqual(Guid.Empty).WithMessage("Invalid team ID")
            .When(x => x.TeamId.HasValue);

        RuleFor(x => x.WebhookSecret)
            .MaximumLength(Integration.MaxWebhookSecretLength)
            .WithMessage($"Webhook secret cannot exceed {Integration.MaxWebhookSecretLength} characters")
            .When(x => !string.IsNullOrEmpty(x.WebhookSecret));

        RuleFor(x => x.WebhookSignatureHeader)
            .MaximumLength(Integration.MaxWebhookSignatureHeaderLength)
            .WithMessage($"Signature header cannot exceed {Integration.MaxWebhookSignatureHeaderLength} characters")
            .When(x => !string.IsNullOrEmpty(x.WebhookSignatureHeader));

        RuleFor(x => x.WebhookSignatureHeader)
            .NotEmpty().WithMessage("A signature header name is required when a secret is set")
            .When(x => !string.IsNullOrEmpty(x.WebhookSecret));
    }
}
