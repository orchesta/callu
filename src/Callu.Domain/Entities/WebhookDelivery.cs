using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// One row per outbound webhook attempt (incident ACK callbacks).
/// Replaces the previous "log-and-forget" model so the UI can surface a
/// delivery history per incident and a Quartz job can re-attempt 5xx/connect
/// failures with exponential backoff.
/// </summary>
public class WebhookDelivery : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Guid? ServiceId { get; set; }

    /// <summary>"Outbound" today; "Inbound" reserved for a future capture surface.</summary>
    [Required, StringLength(20)]
    public string Direction { get; set; } = "Outbound";

    [Required, StringLength(500)]
    public string Url { get; set; } = string.Empty;

    /// <summary>"acknowledge" | "resolve" — passed through from EscalationOrchestrator.</summary>
    [StringLength(50)]
    public string? AckType { get; set; }

    public int? HttpStatus { get; set; }

    /// <summary>First 1 KiB of the body actually sent; clipped to avoid bloat.</summary>
    [StringLength(1024)]
    public string? RequestBodySample { get; set; }

    /// <summary>First 1 KiB of the response body; clipped.</summary>
    [StringLength(1024)]
    public string? ResponseBodySample { get; set; }

    [StringLength(1000)]
    public string? Error { get; set; }

    public int AttemptCount { get; set; } = 1;

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the retry job should re-fire; null once terminal.</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>Stored as its string name (varchar) via a value converter — see ApplicationDbContext.</summary>
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
}
