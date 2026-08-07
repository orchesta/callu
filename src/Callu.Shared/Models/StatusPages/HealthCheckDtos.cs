namespace Callu.Shared.Models.StatusPages;

/// <summary>
/// Health check execution result
/// </summary>
public record HealthCheckResultDto
{
    public Guid ComponentId { get; init; }
    public string Status { get; init; } = "operational";
    public int? ResponseMs { get; init; }
    public string? Message { get; init; }
    public DateTime CheckedAt { get; init; }
}

/// <summary>
/// Sniffer result — captured response for template building
/// </summary>
public record HealthCheckSnifferResultDto
{
    public Guid ComponentId { get; init; }
    public int HttpStatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public string? ContentType { get; init; }
    public int ResponseMs { get; init; }
    public Dictionary<string, string> ResponseHeaders { get; init; } = new();
}
