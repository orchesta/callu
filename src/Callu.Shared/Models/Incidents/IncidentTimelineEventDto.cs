namespace Callu.Shared.Models.Incidents;

/// <summary>
/// DTO for incident timeline events (call-related and general)
/// </summary>
public class IncidentTimelineEventDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ActorName { get; set; }
    public DateTime CreatedAt { get; set; }
}
