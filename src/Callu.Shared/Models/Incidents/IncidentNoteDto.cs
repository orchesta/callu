namespace Callu.Shared.Models.Incidents;

/// <summary>
/// Incident note DTO
/// </summary>
public record IncidentNoteDto
{
    public Guid Id { get; init; }
    public Guid IncidentId { get; init; }
    public string Content { get; init; } = string.Empty;
    public bool IsInternal { get; init; }
    public bool IsPinned { get; init; }
    public string? CreatedBy { get; init; }

    /// <summary>Display name of the author; null when the user can no longer be resolved.</summary>
    public string? CreatedByName { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Create incident note request
/// </summary>
public record CreateIncidentNoteRequest
{
    public string Content { get; init; } = string.Empty;
    public bool IsInternal { get; init; }
}

/// <summary>
/// Update incident note request
/// </summary>
public record UpdateIncidentNoteRequest
{
    public string Content { get; init; } = string.Empty;
    public bool IsPinned { get; init; }
}
