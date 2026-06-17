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
    public string? ExternalAlertId { get; init; }
    public string? DataLanguage { get; init; }
}
