namespace Callu.Shared.Models.Services;

/// <summary>
/// Service DTO for list views
/// </summary>
public record ServiceDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Type { get; init; } = "Api";
    public string Status { get; init; } = "Operational";
    public string? Environment { get; init; }
    public double Uptime { get; init; } = 100.0;
    public string? Color { get; init; }
    public string? Icon { get; init; }
    public bool IsPublic { get; init; } = true;
    public int DisplayOrder { get; init; }
    public Guid? TeamId { get; init; }
    public string? TeamName { get; init; }
    public int IncidentCount { get; init; }
    public bool? WebhookEnabled { get; init; }
    public Guid? WebhookTemplateId { get; init; }
    public DateTime CreatedAt { get; init; }

    public bool AckEnabled { get; init; }
    public string? AckUrl { get; init; }
    public string AckHttpMethod { get; init; } = "POST";
    public string AckContentType { get; init; } = "application/json";
    public string? AckHeaders { get; init; }
    public string? AckPayloadTemplate { get; init; }
}