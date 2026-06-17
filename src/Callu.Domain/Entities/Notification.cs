using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents a notification sent to a user
/// </summary>
public class Notification : BaseEntity
{
    /// <summary>
    /// User ID to notify
    /// </summary>
    [Required]
    [StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Notification type/channel
    /// </summary>
    public NotificationType Type { get; set; }

    /// <summary>
    /// Notification title
    /// </summary>
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Notification body/message
    /// </summary>
    [StringLength(2000)]
    public string? Message { get; set; }

    /// <summary>
    /// Link/URL for the notification (e.g., incident detail page)
    /// </summary>
    [StringLength(500)]
    public string? ActionUrl { get; set; }

    /// <summary>
    /// Related incident ID (if applicable)
    /// </summary>
    public Guid? IncidentId { get; set; }

    /// <summary>
    /// Navigation property for incident
    /// </summary>
    public virtual Incident? Incident { get; set; }

    /// <summary>
    /// Is the notification read
    /// </summary>
    public bool IsRead { get; set; } = false;

    /// <summary>
    /// When the notification was read
    /// </summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>
    /// Is the notification sent successfully
    /// </summary>
    public bool IsSent { get; set; } = false;

    /// <summary>
    /// When the notification was sent
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// Delivery status lifecycle tracking
    /// </summary>
    public NotificationDeliveryStatus DeliveryStatus { get; set; } = NotificationDeliveryStatus.Pending;

    /// <summary>
    /// When the last delivery attempt was made
    /// </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// Error message if sending failed
    /// </summary>
    [StringLength(500)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// UTC time after which the next retry is allowed. Set by
    /// <see cref="MarkFailed(string)"/> using exponential backoff so a down
    /// provider does not get hammered every queue tick.
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Idempotency key — unique per deterministic dispatch attempt
    /// (incidentId + userId + type + eventType + escalationLevel).
    /// Enforced by a unique index at the database level so worker crashes or
    /// duplicate dispatch paths cannot produce two notifications for the
    /// same logical event.
    /// </summary>
    [StringLength(200)]
    public string? DedupeKey { get; set; }

    /// <summary>
    /// Maximum retry attempts before marking as permanently failed
    /// </summary>
    public const int MaxRetries = 3;

    /// <summary>
    /// Mark notification as successfully delivered
    /// </summary>
    public void MarkDelivered()
    {
        IsSent = true;
        SentAt = DateTime.UtcNow;
        DeliveryStatus = NotificationDeliveryStatus.Delivered;
        LastAttemptAt = DateTime.UtcNow;
        NextRetryAt = null;
        ErrorMessage = null;
    }

    /// <summary>
    /// Mark notification delivery as failed, with exponential backoff scheduling the next retry.
    /// Retry schedule (from attempt 1): ~30s, 2m, 8m — capped at <see cref="MaxRetries"/>.
    /// </summary>
    public void MarkFailed(string error)
    {
        var now = DateTime.UtcNow;
        LastAttemptAt = now;
        ErrorMessage = error;
        RetryCount++;

        if (RetryCount >= MaxRetries)
        {
            DeliveryStatus = NotificationDeliveryStatus.PermanentlyFailed;
            NextRetryAt = null;
        }
        else
        {
            DeliveryStatus = NotificationDeliveryStatus.Failed;
            var backoffSeconds = 30 * Math.Pow(4, RetryCount - 1);
            var clamped = Math.Min(backoffSeconds, 3600);
            NextRetryAt = now.AddSeconds(clamped);
        }
    }

    /// <summary>
    /// Mark notification as terminally failed regardless of remaining retry budget.
    /// Use for non-retryable errors (user not found, missing contact info, SMTP unconfigured).
    /// </summary>
    public void MarkPermanentlyFailed(string error)
    {
        LastAttemptAt = DateTime.UtcNow;
        ErrorMessage = error;
        DeliveryStatus = NotificationDeliveryStatus.PermanentlyFailed;
        NextRetryAt = null;
    }

    /// <summary>
    /// Mark notification as currently being sent
    /// </summary>
    public void MarkSending()
    {
        DeliveryStatus = RetryCount > 0
            ? NotificationDeliveryStatus.Retrying
            : NotificationDeliveryStatus.Sending;
        LastAttemptAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark notification as intentionally not sent because the channel is
    /// offline (SMTP unconfigured, no SMS/voice provider). Distinct from
    /// PermanentlyFailed (data problem) so retry queues ignore it AND
    /// metrics aren't inflated with false "delivered" counts. Fix 05.F7.
    /// </summary>
    public void MarkSkipped(string reason)
    {
        LastAttemptAt = DateTime.UtcNow;
        ErrorMessage = reason;
        DeliveryStatus = NotificationDeliveryStatus.Skipped;
        NextRetryAt = null;
    }
}
