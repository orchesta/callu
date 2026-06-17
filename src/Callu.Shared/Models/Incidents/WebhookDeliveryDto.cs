using Callu.Domain.Enums;

namespace Callu.Shared.Models.Incidents;

/// <summary>
/// Read-side projection of <c>WebhookDelivery</c> for the incident detail panel.
/// Bodies are intentionally pre-clipped to 1 KiB on the entity so the API
/// response stays small even with many attempts. <c>Status</c> serializes as its
/// string name (JsonStringEnumConverter), so the wire shape is unchanged.
/// </summary>
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
