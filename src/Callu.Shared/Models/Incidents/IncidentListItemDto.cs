namespace Callu.Shared.Models.Incidents;

/// <summary>
/// Incident list item DTO (lightweight)
/// </summary>
public record IncidentListItemDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Severity { get; init; } = "Medium";
    public string Status { get; init; } = "Open";
    public DateTime StartedAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public string? ServiceName { get; init; }
    public string? TeamName { get; init; }
    public string? AcknowledgedBy { get; init; }
    public string? ResolvedBy { get; init; }
}