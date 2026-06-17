namespace Callu.Application.Services;

/// <summary>
/// Parses health check HTTP responses using configured template mappings.
/// Mirrors the IWebhookPayloadParser pattern for health check responses.
/// </summary>
public interface IHealthCheckResponseParser
{
    /// <summary>
    /// Parse a health check response body using field mappings and state mapping.
    /// Returns the determined component status (operational, degraded, partial_outage, major_outage).
    /// </summary>
    HealthCheckParseResult Parse(string responseBody, int httpStatusCode, string? fieldMappings, string? stateMapping);
}

/// <summary>
/// Result of parsing a health check response
/// </summary>
public class HealthCheckParseResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = "operational";
    public string? Message { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, string?> ExtractedFields { get; set; } = new();
}
