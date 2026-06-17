namespace Callu.Shared.Models.Incidents;

/// <summary>
/// Full incident DTO with details
/// </summary>
public record IncidentDto : IncidentListItemDto
{
    public string? Description { get; init; }
    public Guid? ServiceId { get; init; }
    public Guid? TeamId { get; init; }
    public string DataLanguage { get; init; } = "en-US";
    public DateTime CreatedAt { get; init; }
}