using FluentValidation;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Services;

namespace Callu.Application.Validators;

public class CreateServiceRequestValidator : AbstractValidator<CreateServiceRequest>
{
    public CreateServiceRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Service name is required")
            .MaximumLength(100).WithMessage("Service name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Service type is required")
            .IsEnumName(typeof(ServiceType), caseSensitive: false)
            .WithMessage("Invalid service type");

        RuleFor(x => x.TeamId)
            .NotEqual(Guid.Empty).WithMessage("Invalid team ID")
            .When(x => x.TeamId.HasValue);

        RuleFor(x => x.AckUrl)
            .MaximumLength(Service.MaxAckUrlLength)
            .When(x => x.AckUrl is not null);

        RuleFor(x => x.AckHttpMethod)
            .MaximumLength(Service.MaxAckHttpMethodLength)
            .Must(ServiceAckRules.BeAnAllowedMethod)
            .WithMessage("ACK HTTP method must be POST, PUT or PATCH")
            .When(x => x.AckHttpMethod is not null);

        RuleFor(x => x.AckContentType)
            .NotEmpty()
            .MaximumLength(Service.MaxAckContentTypeLength)
            .Must(ServiceAckRules.BeABareMediaType)
            .WithMessage("Content type must be a bare type/subtype value")
            .When(x => x.AckContentType is not null);

        RuleFor(x => x.AckHeaders)
            .MaximumLength(Service.MaxAckHeadersLength)
            .Must(ServiceAckRules.BeAJsonObjectOfStrings)
            .WithMessage("ACK headers must be a JSON object of string values")
            .When(x => !string.IsNullOrEmpty(x.AckHeaders));

        RuleFor(x => x.AckPayloadTemplate)
            .MaximumLength(Service.MaxAckPayloadTemplateLength)
            .When(x => x.AckPayloadTemplate is not null);

        RuleFor(x => x.AckEvents)
            .Must(ServiceAckRules.BeDefinedEventFlags)
            .WithMessage("ACK events contains undefined flags")
            .When(x => x.AckEvents.HasValue);

        RuleFor(x => x.AckSecret)
            .MaximumLength(Service.MaxAckSecretLength)
            .When(x => x.AckSecret is not null);

        RuleFor(x => x.AckSignatureHeader)
            .MaximumLength(Service.MaxAckSignatureHeaderLength)
            .When(x => x.AckSignatureHeader is not null);
    }
}
