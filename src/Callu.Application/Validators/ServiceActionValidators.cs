using FluentValidation;
using Callu.Domain.Entities;
using Callu.Shared.Models.Services;

namespace Callu.Application.Validators;

public class CreateServiceActionRequestValidator : AbstractValidator<CreateServiceActionRequest>
{
    public CreateServiceActionRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(ServiceAction.MaxNameLength);

        RuleFor(x => x.Description)
            .MaximumLength(ServiceAction.MaxDescriptionLength)
            .When(x => x.Description is not null);

        RuleFor(x => x.Url)
            .NotEmpty()
            .MaximumLength(ServiceAction.MaxUrlLength)
            .Must(ServiceActionRules.BeAnAbsoluteHttpUrl)
            .WithMessage("Action URL must be an absolute http(s) URL");

        RuleFor(x => x.HttpMethod)
            .MaximumLength(ServiceAction.MaxHttpMethodLength)
            .Must(ServiceActionRules.BeAnAllowedMethod)
            .WithMessage("Action HTTP method must be GET, POST, PUT or PATCH");

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .MaximumLength(ServiceAction.MaxContentTypeLength)
            .Must(ServiceAckRules.BeABareMediaType)
            .WithMessage("Content type must be a bare type/subtype value");

        RuleFor(x => x.HeadersJson)
            .MaximumLength(ServiceAction.MaxHeadersLength)
            .Must(ServiceAckRules.BeAJsonObjectOfStrings)
            .WithMessage("Action headers must be a JSON object of string values")
            .When(x => !string.IsNullOrEmpty(x.HeadersJson));

        RuleFor(x => x.PayloadTemplate)
            .MaximumLength(ServiceAction.MaxPayloadTemplateLength)
            .When(x => x.PayloadTemplate is not null);

        RuleFor(x => x.Secret)
            .MaximumLength(ServiceAction.MaxSecretLength)
            .When(x => x.Secret is not null);

        RuleFor(x => x.SignatureHeader)
            .MaximumLength(ServiceAction.MaxSignatureHeaderLength)
            .When(x => x.SignatureHeader is not null);
    }
}

public class UpdateServiceActionRequestValidator : AbstractValidator<UpdateServiceActionRequest>
{
    public UpdateServiceActionRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(ServiceAction.MaxNameLength)
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(ServiceAction.MaxDescriptionLength)
            .When(x => x.Description is not null);

        RuleFor(x => x.Url)
            .NotEmpty()
            .MaximumLength(ServiceAction.MaxUrlLength)
            .Must(ServiceActionRules.BeAnAbsoluteHttpUrl)
            .WithMessage("Action URL must be an absolute http(s) URL")
            .When(x => x.Url is not null);

        RuleFor(x => x.HttpMethod)
            .MaximumLength(ServiceAction.MaxHttpMethodLength)
            .Must(ServiceActionRules.BeAnAllowedMethod)
            .WithMessage("Action HTTP method must be GET, POST, PUT or PATCH")
            .When(x => x.HttpMethod is not null);

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .MaximumLength(ServiceAction.MaxContentTypeLength)
            .Must(ServiceAckRules.BeABareMediaType)
            .WithMessage("Content type must be a bare type/subtype value")
            .When(x => x.ContentType is not null);

        RuleFor(x => x.HeadersJson)
            .MaximumLength(ServiceAction.MaxHeadersLength)
            .Must(ServiceAckRules.BeAJsonObjectOfStrings)
            .WithMessage("Action headers must be a JSON object of string values")
            .When(x => !string.IsNullOrEmpty(x.HeadersJson));

        RuleFor(x => x.PayloadTemplate)
            .MaximumLength(ServiceAction.MaxPayloadTemplateLength)
            .When(x => x.PayloadTemplate is not null);

        RuleFor(x => x.Secret)
            .MaximumLength(ServiceAction.MaxSecretLength)
            .When(x => x.Secret is not null);

        RuleFor(x => x.SignatureHeader)
            .MaximumLength(ServiceAction.MaxSignatureHeaderLength)
            .When(x => x.SignatureHeader is not null);
    }
}

internal static class ServiceActionRules
{
    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH"];

    internal static bool BeAnAllowedMethod(string? method) =>
        method is not null && AllowedMethods.Contains(method, StringComparer.OrdinalIgnoreCase);

    internal static bool BeAnAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
