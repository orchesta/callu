namespace Callu.Shared.Models.Webhooks;

/// <summary>
/// Webhook processing result
/// </summary>
public record WebhookProcessResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public Guid? IncidentId { get; init; }
    public Guid? CaptureId { get; init; }
    public bool WasCaptured { get; init; }
    public bool Deduplicated { get; init; }
}
