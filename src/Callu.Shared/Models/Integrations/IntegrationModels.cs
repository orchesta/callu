namespace Callu.Shared.Models.Integrations;

/// <summary>
/// An inbound integration: one named webhook endpoint feeding one service.
/// </summary>
public record IntegrationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ServiceId { get; init; }
    public string? ServiceName { get; init; }
    public Guid? TeamId { get; init; }
    public string? TeamName { get; init; }
    public Guid? WebhookTemplateId { get; init; }
    public string? WebhookTemplateName { get; init; }
    public bool IsActive { get; init; }
    public bool WebhookEnabled { get; init; }
    public bool ListeningMode { get; init; }
    public int CapturedCount { get; init; }
    public bool HasToken { get; init; }
    /// <summary>Path the sender posts to. Contains the token, so it is only returned to admins.</summary>
    public string? WebhookUrl { get; init; }
    public bool HasApiKey { get; init; }
    public string? MaskedApiKey { get; init; }
    /// <summary>True when an HMAC signing secret is configured; the secret itself is never returned.</summary>
    public bool HasSignatureSecret { get; init; }
    public string? SignatureHeaderName { get; init; }
    public DateTime? LastWebhookReceivedAt { get; init; }
    public int WebhooksReceivedCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Credentials handed back once, at create or rotate time.
/// </summary>
public record IntegrationSecretsDto
{
    public Guid Id { get; init; }
    public string WebhookUrl { get; init; } = string.Empty;
    public string WebhookToken { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}

public class CreateIntegrationRequest
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Webhook";
    public string? Description { get; set; }
    /// <summary>Absent means the endpoint starts unbound and captures everything it receives.</summary>
    public Guid? ServiceId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? WebhookTemplateId { get; set; }
    public bool? ListeningMode { get; set; }
    public string? WebhookSecret { get; set; }
    public string? WebhookSignatureHeader { get; set; }
}

public class BindIntegrationServiceRequest
{
    /// <summary>Target service; null unbinds the endpoint back to capture-only.</summary>
    public Guid? ServiceId { get; set; }
}

public class UpdateIntegrationRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? WebhookTemplateId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool WebhookEnabled { get; set; } = true;
    /// <summary>Null leaves listening mode untouched, so an older client cannot switch it off.</summary>
    public bool? ListeningMode { get; set; }
    /// <summary>Null leaves the current secret untouched; empty string clears it.</summary>
    public string? WebhookSecret { get; set; }
    public string? WebhookSignatureHeader { get; set; }
}
