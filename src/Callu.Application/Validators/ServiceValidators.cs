using System.Text.Json;
using FluentValidation;
using Callu.Domain.Entities;
using Callu.Shared.Models.Services;

namespace Callu.Application.Validators;

public class UpdateServiceRequestValidator : AbstractValidator<UpdateServiceRequest>
{
    public UpdateServiceRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100)
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x.AckUrl)
            .MaximumLength(Service.MaxAckUrlLength)
            .When(x => x.AckUrl is not null);

        RuleFor(x => x.AckHttpMethod)
            .MaximumLength(Service.MaxAckHttpMethodLength)
            .Must(ServiceAckRules.BeAnAllowedMethod)
            .WithMessage("ACK HTTP method must be POST, PUT or PATCH")
            .When(x => x.AckHttpMethod is not null);

        RuleFor(x => x.Type)
            .IsEnumName(typeof(Domain.Enums.ServiceType), caseSensitive: false)
            .When(x => x.Type is not null);

        RuleFor(x => x.Status)
            .IsEnumName(typeof(Domain.Enums.ServiceStatus), caseSensitive: false)
            .When(x => x.Status is not null);

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

public class CreateServiceDependencyRequestValidator : AbstractValidator<CreateServiceDependencyRequest>
{
    public CreateServiceDependencyRequestValidator()
    {
        RuleFor(x => x.DependsOnServiceId)
            .NotEmpty().WithMessage("Dependency service ID is required");
    }
}

internal static class ServiceAckRules
{
    private static readonly string[] AllowedMethods = ["POST", "PUT", "PATCH"];

    private const int AllEventBits =
        (int)(Domain.Enums.ServiceAckEvents.Created
              | Domain.Enums.ServiceAckEvents.Acknowledged
              | Domain.Enums.ServiceAckEvents.Resolved
              | Domain.Enums.ServiceAckEvents.Closed
              | Domain.Enums.ServiceAckEvents.Reopened);

    internal static bool BeDefinedEventFlags(int? events) =>
        events.HasValue && (events.Value & ~AllEventBits) == 0 && events.Value >= 0;

    internal static bool BeAnAllowedMethod(string? method) =>
        method is not null && AllowedMethods.Contains(method, StringComparer.OrdinalIgnoreCase);

    internal static bool BeABareMediaType(string? value) =>
        value is not null
        && System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(value, out var parsed)
        && string.Equals(parsed.MediaType, value, StringComparison.OrdinalIgnoreCase);

    internal static bool BeAJsonObjectOfStrings(string? headers)
    {
        if (string.IsNullOrEmpty(headers)) return true;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(headers) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
