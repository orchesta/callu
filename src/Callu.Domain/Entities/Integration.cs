using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents an external integration for receiving/sending data
/// </summary>
public class Integration : BaseEntity
{

    /// <summary>
    /// Integration name
    /// </summary>
    [Required]
    [StringLength(100)]
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
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// API key for authentication (encrypted)
    /// </summary>
    [StringLength(500)]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Webhook secret for validating incoming requests
    /// </summary>
    [StringLength(500)]
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
    /// Last time a webhook was received
    /// </summary>
    public DateTime? LastWebhookReceivedAt { get; set; }

    /// <summary>
    /// Total webhooks received count
    /// </summary>
    public int WebhooksReceivedCount { get; set; } = 0;
}
