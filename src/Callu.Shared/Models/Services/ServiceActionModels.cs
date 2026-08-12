namespace Callu.Shared.Models.Services;

/// <summary>
/// Operator-defined outbound action on a service
/// </summary>
public record ServiceActionDto
{
    public Guid Id { get; init; }
    public Guid ServiceId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Url { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = "POST";
    public string ContentType { get; init; } = "application/json";
    public string? HeadersJson { get; init; }
    public string? PayloadTemplate { get; init; }
    public bool HasSecret { get; init; }
    public string? SignatureHeader { get; init; }
    public bool IsEnabled { get; init; }
    public int DisplayOrder { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Create service action request
/// </summary>
public record CreateServiceActionRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Url { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = "POST";
    public string ContentType { get; init; } = "application/json";
    public string? HeadersJson { get; init; }
    public string? PayloadTemplate { get; init; }
    public string? Secret { get; init; }
    public string? SignatureHeader { get; init; }
    public bool IsEnabled { get; init; } = true;
    public int DisplayOrder { get; init; }
}

/// <summary>
/// Update service action request; omitted fields stay unchanged, an empty-string secret clears it
/// </summary>
public record UpdateServiceActionRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? HttpMethod { get; init; }
    public string? ContentType { get; init; }
    public string? HeadersJson { get; init; }
    public string? PayloadTemplate { get; init; }
    public string? Secret { get; init; }
    public string? SignatureHeader { get; init; }
    public bool? IsEnabled { get; init; }
    public int? DisplayOrder { get; init; }
}

/// <summary>
/// Synchronous result of one manual action execution
/// </summary>
public record ServiceActionExecutionResult
{
    public required string Outcome { get; init; }
    public Guid? DeliveryId { get; init; }
    public int? HttpStatus { get; init; }
    public string? Error { get; init; }
    public string? ResponseBodySample { get; init; }
}
