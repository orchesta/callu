namespace Callu.Shared.Models.Incidents;

/// <summary>
/// Filter for incident list with pagination support
/// </summary>
public record IncidentFilter
{
    public string? Status { get; init; }
    public string? Severity { get; init; }
    public Guid? ServiceId { get; init; }
    public Guid? TeamId { get; init; }
    public string? SearchQuery { get; init; }
    
    /// <summary>
    /// Page number (1-based, default: 1)
    /// </summary>
    public int Page { get; init; } = 1;
    
    /// <summary>
    /// Items per page (default: 25, max: 100)
    /// </summary>
    public int PageSize { get; init; } = 25;
}
