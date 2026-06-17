namespace Callu.Shared.Models.Webhooks;

/// <summary>
/// Webhook capture DTO
/// </summary>
public record WebhookCaptureDto
{
    public Guid Id { get; init; }
    public Guid ServiceId { get; init; }
    public DateTime CapturedAt { get; init; }
    public string Method { get; init; } = "POST";
    public string? ContentType { get; init; }
    public string? SourceIp { get; init; }
    public string Headers { get; init; } = "{}";
    public string Body { get; init; } = string.Empty;
    public int BodySize { get; init; }
    public string Status { get; init; } = "Captured";
}
