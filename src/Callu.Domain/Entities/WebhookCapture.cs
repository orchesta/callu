using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Stores captured webhook requests for learning/discovery mode
/// </summary>
public class WebhookCapture : BaseEntity
{
    /// <summary>
    /// Service this capture belongs to
    /// </summary>
    public Guid ServiceId { get; set; }
    
    /// <summary>
    /// Navigation property for service
    /// </summary>
    public virtual Service Service { get; set; } = null!;
    
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
