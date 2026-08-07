using Callu.Domain.Enums;

namespace Callu.Infrastructure.Services.Models;

/// <summary>
/// Result of parsing a webhook payload
/// </summary>
public class ParsedWebhookPayload
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Medium;
    public WebhookState State { get; set; } = WebhookState.Open;
    public string? ExternalId { get; set; }
}
