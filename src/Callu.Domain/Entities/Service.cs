using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents a service in the service catalog
/// </summary>
public class Service : BaseEntity
{

    /// <summary>
    /// Service name
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Service description
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Type of service
    /// </summary>
    public ServiceType Type { get; set; } = ServiceType.Api;

    /// <summary>
    /// Environment (Production, Staging, Development)
    /// </summary>
    [StringLength(50)]
    public string? Environment { get; set; }

    /// <summary>
    /// Current health status
    /// </summary>
    public ServiceStatus Status { get; set; } = ServiceStatus.Operational;

    /// <summary>
    /// Service color for UI
    /// </summary>
    [StringLength(20)]
    public string? Color { get; set; }

    /// <summary>
    /// Service icon
    /// </summary>
    [StringLength(50)]
    public string? Icon { get; set; }

    /// <summary>
    /// Is this service visible on status pages
    /// </summary>
    public bool IsPublic { get; set; } = true;

    /// <summary>
    /// Display order
    /// </summary>
    public int DisplayOrder { get; set; } = 0;

    /// <summary>
    /// Owner team ID
    /// </summary>
    public Guid? TeamId { get; set; }

    /// <summary>
    /// Navigation property for owner team
    /// </summary>
    public virtual Team? Team { get; set; }

    /// <summary>
    /// Incidents related to this service
    /// </summary>
    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    /// <summary>
    /// Integrations for this service
    /// </summary>
    public virtual ICollection<Integration> Integrations { get; set; } = new List<Integration>();

    /// <summary>
    /// Services this service depends on
    /// </summary>
    public virtual ICollection<ServiceDependency> Dependencies { get; set; } = new List<ServiceDependency>();

    /// <summary>
    /// Services that depend on this service
    /// </summary>
    public virtual ICollection<ServiceDependency> DependentServices { get; set; } = new List<ServiceDependency>();

    /// <summary>
    /// Selected alert provider ID (e.g., "callu", "zabbix", "prometheus")
    /// </summary>
    [StringLength(50)]
    public string? ProviderId { get; set; }
    
    /// <summary>
    /// Provider-specific configuration as JSON
    /// Contains: pollingInterval, ackEndpoint, credentials, etc.
    /// </summary>
    public string? ProviderConfig { get; set; }

    /// <summary>
    /// Whether ACK back is enabled for this service
    /// </summary>
    public bool AckEnabled { get; set; } = false;
    
    /// <summary>
    /// URL to send ACK requests to
    /// </summary>
    [StringLength(500)]
    public string? AckUrl { get; set; }
    
    /// <summary>
    /// HTTP method for ACK request (POST, PUT, PATCH)
    /// </summary>
    [StringLength(10)]
    public string AckHttpMethod { get; set; } = "POST";
    
    /// <summary>
    /// Content-Type header for ACK request
    /// </summary>
    [StringLength(50)]
    public string AckContentType { get; set; } = "application/json";
    
    /// <summary>
    /// Custom headers as JSON object: {"Authorization": "Bearer xxx", "X-Api-Key": "abc"}
    /// </summary>
    public string? AckHeaders { get; set; }
    
    /// <summary>
    /// Scriban template for ACK payload body
    /// </summary>
    public string? AckPayloadTemplate { get; set; }

    /// <summary>
    /// Is webhook receiving enabled for this service (computed from ProviderId)
    /// </summary>
    public bool WebhookEnabled => !string.IsNullOrEmpty(ProviderId);
    
    /// <summary>
    /// Unique token for webhook URL (secure, non-guessable)
    /// Example: /api/webhooks/{this-token}
    /// </summary>
    [StringLength(64)]
    public string? WebhookToken { get; set; }
    
    /// <summary>
    /// API Key for webhook authentication (optional, can be empty during testing)
    /// </summary>
    [StringLength(128)]
    public string? WebhookApiKey { get; set; }
    
    /// <summary>
    /// HMAC secret for webhook signature verification (optional)
    /// When set, incoming webhooks must include a valid HMAC signature
    /// </summary>
    [StringLength(256)]
    public string? WebhookSecret { get; set; }
    
    /// <summary>
    /// Name of the header containing the HMAC signature (e.g., "X-Hub-Signature-256")
    /// Required when WebhookSecret is set
    /// </summary>
    [StringLength(100)]
    public string? WebhookSignatureHeader { get; set; }
    
    /// <summary>
    /// Listening/Capture mode - requests are logged but no incidents created
    /// </summary>
    public bool WebhookListeningMode { get; set; } = false;
    
    /// <summary>
    /// Active webhook template for parsing payloads
    /// </summary>
    public Guid? WebhookTemplateId { get; set; }
    
    /// <summary>
    /// Navigation property for webhook template
    /// </summary>
    public virtual WebhookTemplate? WebhookTemplate { get; set; }
    
    /// <summary>
    /// Captured webhook requests (for learning mode)
    /// </summary>
    public virtual ICollection<WebhookCapture> WebhookCaptures { get; set; } = new List<WebhookCapture>();
    
    /// <summary>
    /// Last webhook received timestamp
    /// </summary>
    public DateTime? LastWebhookReceivedAt { get; set; }
    
    /// <summary>
    /// Total webhook count
    /// </summary>
    public int WebhooksReceivedCount { get; set; } = 0;
}
