namespace Callu.Shared.Models.Incidents;

/// <summary>
/// Create incident request
/// </summary>
public record CreateIncidentRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Severity { get; init; } = "Medium";
    public Guid? ServiceId { get; init; }
    public Guid? TeamId { get; init; }
    /// <summary>Set by inbound-integration webhooks; left null for manual / service-token creates.</summary>
    public Guid? SourceIntegrationId { get; init; }
    public string? ExternalAlertId { get; init; }
    public string? DataLanguage { get; init; }
}
