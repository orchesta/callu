using Callu.Domain.Enums;

namespace Callu.Shared.Models.Incidents;

/// <summary>Read-side projection of a webhook delivery for the incident detail panel.</summary>
public sealed record WebhookDeliveryDto(
    Guid Id,
    Guid IncidentId,
    Guid? ServiceId,
    string Url,
    string? AckType,
    int? HttpStatus,
    string? Error,
    int AttemptCount,
    DateTime AttemptedAt,
    DateTime? NextRetryAt,
    WebhookDeliveryStatus Status,
    string? ResponseBodySample);
