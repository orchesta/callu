namespace Callu.Shared.Models.Incidents;

/// <summary>
/// Update incident request
/// </summary>
public record UpdateIncidentRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Severity { get; init; }
    public string? Status { get; init; }
    public Guid? ServiceId { get; init; }
    public Guid? TeamId { get; init; }
}