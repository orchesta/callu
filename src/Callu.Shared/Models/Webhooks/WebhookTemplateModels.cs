namespace Callu.Shared.Models.Webhooks;

/// <summary>
/// Webhook template DTOs — response, create request, and update request
/// </summary>

public record WebhookTemplateDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string FieldMappings { get; init; } = "{}";
    public string? StateMapping { get; init; }
    public string? SamplePayload { get; init; }
    public string DataLanguage { get; init; } = "en-US";
    public bool IsBuiltIn { get; init; }
    public bool IsActive { get; init; }
    public int UsageCount { get; init; }
}

public record CreateWebhookTemplateRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string FieldMappings { get; init; } = "{}";
    public string? StateMapping { get; init; }
    public string? SamplePayload { get; init; }
    public string? DataLanguage { get; init; }
}

public record UpdateWebhookTemplateRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? FieldMappings { get; init; }
    public string? StateMapping { get; init; }
    public string? SamplePayload { get; init; }
    public string? DataLanguage { get; init; }
    public bool? IsActive { get; init; }
}
