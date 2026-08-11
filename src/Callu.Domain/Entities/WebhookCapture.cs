using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Stores captured webhook requests for learning/discovery mode
/// </summary>
public class WebhookCapture : BaseEntity
{
    /// <summary>How many captures one endpoint keeps; the oldest beyond this are dropped on write.</summary>
    public const int MaxPerScope = 500;

    /// <summary>Largest page a capture listing returns.</summary>
    public const int MaxPageSize = 50;

    /// <summary>Suffix appended to a captured body that was cut at the size limit.</summary>
    public const string TruncationSuffix = "\n...[truncated]";

    /// <summary>
    /// Service this capture belongs to (absent for captures taken on an unbound integration)
    /// </summary>
    public Guid? ServiceId { get; set; }

    /// <summary>
    /// Navigation property for service
    /// </summary>
    public virtual Service? Service { get; set; }

    /// <summary>
    /// Integration this capture arrived through (absent for captures taken on a service token)
    /// </summary>
    public Guid? IntegrationId { get; set; }

    /// <summary>
    /// Navigation property for integration
    /// </summary>
    public virtual Integration? Integration { get; set; }
    
    /// <summary>
    /// When the request was captured
    /// </summary>
    public DateTime CapturedAt { get; set; }
    
    /// <summary>
    /// HTTP method (POST, PUT, etc.)
    /// </summary>
    [StringLength(10)]
    public string Method { get; set; } = "POST";
    
    /// <summary>
    /// Content-Type header
    /// </summary>
    [StringLength(100)]
    public string? ContentType { get; set; }
    
    /// <summary>
    /// Source IP address
    /// </summary>
    [StringLength(50)]
    public string? SourceIp { get; set; }
    
    /// <summary>
    /// Request headers as JSON
    /// </summary>
    public string Headers { get; set; } = "{}";
    
    /// <summary>
    /// Raw request body
    /// </summary>
    public string Body { get; set; } = string.Empty;
    
    /// <summary>
    /// Current status of this capture
    /// </summary>
    public WebhookCaptureStatus Status { get; set; } = WebhookCaptureStatus.Captured;
}
