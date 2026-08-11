using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents an external integration for receiving/sending data
/// </summary>
public class Integration : BaseEntity
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxWebhookTokenLength = 64;
    public const int MaxApiKeyLength = 500;
    public const int MaxWebhookSecretLength = 500;
    public const int MaxWebhookSignatureHeaderLength = 100;

    /// <summary>
    /// Integration name
    /// </summary>
    [Required]
    [StringLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type of integration
    /// </summary>
    public IntegrationType Type { get; set; }

    /// <summary>
    /// Integration mode (WebhookOnly, FullApi, AutoSetup)
    /// </summary>
    public IntegrationMode Mode { get; set; } = IntegrationMode.WebhookOnly;

    /// <summary>
    /// Description
    /// </summary>
    [StringLength(MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>
    /// API key the sender must present on inbound webhooks
    /// </summary>
    [StringLength(MaxApiKeyLength)]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Webhook secret for validating incoming requests
    /// </summary>
    [StringLength(MaxWebhookSecretLength)]
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// System-generated inbound webhook URL
    /// </summary>
    [StringLength(500)]
    public string? InboundWebhookUrl { get; set; }

    /// <summary>
    /// Outbound webhook URL for sending notifications
    /// </summary>
    [StringLength(500)]
    public string? OutboundWebhookUrl { get; set; }

    /// <summary>
    /// Payload mapping for parsing incoming webhooks (JSON)
    /// </summary>
    public string? PayloadMapping { get; set; }

    /// <summary>
    /// Provider-specific configuration (JSON)
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// Current state for polling integrations (JSON)
    /// </summary>
    public string? PollingState { get; set; }

    /// <summary>
    /// Associated service (optional - for service-specific integrations)
    /// </summary>
    public Guid? ServiceId { get; set; }

    /// <summary>
    /// Navigation property for service
    /// </summary>
    public virtual Service? Service { get; set; }

    /// <summary>
    /// Associated team (optional)
    /// </summary>
    public Guid? TeamId { get; set; }

    /// <summary>
    /// Navigation property for team
    /// </summary>
    public virtual Team? Team { get; set; }

    /// <summary>
    /// Is this integration active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Direction of data flow (Inbound, Outbound, or Bidirectional)
    /// </summary>
    public IntegrationDirection Direction { get; set; } = IntegrationDirection.Inbound;

    /// <summary>
    /// Provider ID that handles this integration (e.g., "callu", "prometheus")
    /// </summary>
    [StringLength(100)]
    public string? ProviderId { get; set; }

    /// <summary>
    /// Is inbound webhook receiving enabled for this integration
    /// </summary>
    public bool WebhookEnabled { get; set; } = true;

    /// <summary>
    /// Listening/Capture mode - requests are logged but no incidents created
    /// </summary>
    public bool ListeningMode { get; set; } = false;

    /// <summary>
    /// Unique token for the inbound webhook URL (/api/v1/webhooks/{token})
    /// </summary>
    [StringLength(MaxWebhookTokenLength)]
    public string? WebhookToken { get; set; }

    /// <summary>
    /// Name of the header carrying the HMAC signature (required when a secret is set)
    /// </summary>
    [StringLength(MaxWebhookSignatureHeaderLength)]
    public string? WebhookSignatureHeader { get; set; }

    /// <summary>
    /// Template used to parse payloads arriving on this integration
    /// </summary>
    public Guid? WebhookTemplateId { get; set; }

    /// <summary>
    /// Navigation property for the webhook template
    /// </summary>
    public virtual WebhookTemplate? WebhookTemplate { get; set; }

    /// <summary>
    /// Last time a webhook was received
    /// </summary>
    public DateTime? LastWebhookReceivedAt { get; set; }

    /// <summary>
    /// Total webhooks received count
    /// </summary>
    public int WebhooksReceivedCount { get; set; } = 0;
}
