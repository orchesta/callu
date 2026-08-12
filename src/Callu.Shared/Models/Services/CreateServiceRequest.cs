namespace Callu.Shared.Models.Services;

/// <summary>
/// Create service request
/// </summary>
public record CreateServiceRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Type { get; init; } = "Api";
    public string? Environment { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
    public bool IsPublic { get; init; } = true;
    public Guid? TeamId { get; init; }

    public bool AckEnabled { get; init; } = false;
    public string? AckUrl { get; init; }
    public string? AckHttpMethod { get; init; }
    public string? AckContentType { get; init; }
    public string? AckHeaders { get; init; }
    public string? AckPayloadTemplate { get; init; }
    public int? AckEvents { get; init; }
    public string? AckSecret { get; init; }
    public string? AckSignatureHeader { get; init; }
}