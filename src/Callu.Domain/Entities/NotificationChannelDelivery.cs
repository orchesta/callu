using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// One row per outbound notification-channel send (Slack / Teams / generic Webhook /
/// channel-email) for an incident lifecycle event. Gives the dispatch the same durable,
/// retryable model the per-user pipeline already has: a transient failure (5xx / timeout /
/// connection) is re-attempted by <c>NotificationChannelDeliveryRetryQuartzJob</c> with
/// exponential backoff; a permanent failure (4xx / bad config) stops immediately.
///
/// Distinct from <see cref="WebhookDelivery"/>, which tracks incident-ACK callbacks and
/// re-fires through IIncidentEventDispatcher — a different transport and payload.
/// </summary>
public class NotificationChannelDelivery : BaseEntity
{
    /// <summary>The NotificationChannel config this attempt targeted.</summary>
    public Guid ChannelId { get; set; }

    public Guid IncidentId { get; set; }
    public Guid? ServiceId { get; set; }

    /// <summary>"incident.created" | "incident.acknowledged" | "incident.resolved".</summary>
    [Required, StringLength(50)]
    public string EventKey { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Title { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Severity { get; set; }

    [Required, StringLength(2000)]
    public string MessageText { get; set; } = string.Empty;

    public int? HttpStatus { get; set; }

    [StringLength(1000)]
    public string? Error { get; set; }

    public int AttemptCount { get; set; } = 1;

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the retry job should re-fire; null once terminal.</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>Stored as its string name (varchar) via a value converter — see ApplicationDbContext.</summary>
    public NotificationChannelDeliveryStatus Status { get; set; } = NotificationChannelDeliveryStatus.Retrying;
}
