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
    /// <summary>Integration the new template is attached to, in the same transaction as the create.</summary>
    public Guid? AttachToIntegrationId { get; init; }
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

/// <summary>Runs unsaved mappings through the real parser, so a template can be tried before it exists.</summary>
public record PreviewWebhookTemplateRequest
{
    public string SamplePayload { get; init; } = string.Empty;
    public string FieldMappings { get; init; } = "{}";
    public string? StateMapping { get; init; }
}
