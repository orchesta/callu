namespace Callu.Shared.Models.Services;

/// <summary>
/// Update service request
/// </summary>
public record UpdateServiceRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Type { get; init; }
    public string? Environment { get; init; }
    public string? Status { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
    public bool? IsPublic { get; init; }
    public Guid? TeamId { get; init; }

    public bool? AckEnabled { get; init; }
    public string? AckUrl { get; init; }
    public string? AckHttpMethod { get; init; }
    public string? AckContentType { get; init; }
    public string? AckHeaders { get; init; }
    public string? AckPayloadTemplate { get; init; }
}