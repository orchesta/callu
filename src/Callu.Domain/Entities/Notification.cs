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

    /// <summary>Inbox-visibility only — the bell-menu hide; dispatch and dedupe never read it.</summary>
    public DateTime? InboxDismissedAt { get; set; }

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

    /// <summary>The bound on <see cref="ErrorMessage"/>, taken from the column so callers never retype it.</summary>
    public const int MaxErrorMessageLength = 500;

    /// <summary>Failure reason, always written through the Mark* methods, which clamp it.</summary>
    [StringLength(MaxErrorMessageLength)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>UTC time after which the next retry is allowed.</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>Idempotency key, one per deterministic dispatch attempt, backed by a unique index.</summary>
    [StringLength(200)]
    public string? DedupeKey { get; set; }

    /// <summary>UTC time this page was reported as having produced no call at all; null means not reported.</summary>
    // A provider can accept a call and never report anything back, which leaves the page looking
    // delivered with nothing on the incident. This stamp is what keeps that report to one.
    public DateTime? UnconfirmedCallReportedAt { get; set; }

    /// <summary>
    /// Maximum retry attempts before marking as permanently failed
    /// </summary>
    public const int MaxRetries = 3;

    /// <summary>A soft-deleted row is a dead row: nothing on this host will send it.</summary>
    private bool IsLiveRow => !IsDeleted;

    /// <summary>The retry sweep's claim predicate, restated on the entity.</summary>
    public bool RetrySweepWillTakeIt =>
        IsLiveRow
        && DeliveryStatus is NotificationDeliveryStatus.Pending or NotificationDeliveryStatus.Failed
        && RetryCount < MaxRetries;

    /// <summary>A page written down and postponed: Pending, with a deadline and its retry budget intact.</summary>
    public bool IsDeferredPage =>
        IsLiveRow
        && DeliveryStatus == NotificationDeliveryStatus.Pending
        && NextRetryAt.HasValue
        && RetryCount < MaxRetries;

    /// <summary>Whether this row put a page on its way, or one is still coming for it.</summary>
    public bool PageIsOnItsWay =>
        IsLiveRow
        && (DeliveryStatus == NotificationDeliveryStatus.Delivered
            || ((DeliveryStatus is NotificationDeliveryStatus.Sending
                     or NotificationDeliveryStatus.Retrying)
                && RetryCount < MaxRetries)
            || (DeliveryStatus == NotificationDeliveryStatus.Failed && RetryCount < MaxRetries)
            || IsDeferredPage);

    /// <summary>Whether a channel is capable of waking a human — an allowlist, so a new channel must be argued in.</summary>
    public static bool ChannelCanPage(NotificationType type) => type
        is NotificationType.Email
        or NotificationType.Sms
        or NotificationType.VoiceCall;

    /// <summary>This row's channel, run through <see cref="ChannelCanPage(NotificationType)"/>.</summary>
    public bool IsPagingChannel => ChannelCanPage(Type);

    /// <summary>Whether this row counts towards an escalation step's reached — both halves must hold.</summary>
    public bool CountsAsReached => IsPagingChannel && PageIsOnItsWay;

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
        ErrorMessage = Clamp(error);
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

    /// <summary>Shortest wait after an attempt whose outcome could not be determined.</summary>
    // Long enough that a call which really was placed has rung, been answered or given up before the
    // next attempt: re-sending in thirty seconds is how one ambiguous timeout rings a phone twice.
    public static readonly TimeSpan UndeterminedRetryFloor = TimeSpan.FromSeconds(120);

    /// <summary>Mark an attempt whose outcome is unknown: it spends budget like a failure, but the next attempt waits longer.</summary>
    public void MarkUndetermined(string error)
    {
        MarkFailed(error);

        if (NextRetryAt is not { } scheduled || LastAttemptAt is not { } attemptedAt) return;

        var floor = attemptedAt.Add(UndeterminedRetryFloor);
        NextRetryAt = scheduled > floor ? scheduled : floor;
    }

    /// <summary>
    /// Mark notification as terminally failed regardless of remaining retry budget.
    /// Use for non-retryable errors (user not found, missing contact info, SMTP unconfigured).
    /// </summary>
    public void MarkPermanentlyFailed(string error)
    {
        LastAttemptAt = DateTime.UtcNow;
        ErrorMessage = Clamp(error);
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
    /// Mark notification as intentionally not sent because the channel is offline
    /// (SMTP unconfigured, no SMS/voice provider); distinct from PermanentlyFailed.
    /// </summary>
    public void MarkSkipped(string reason)
    {
        LastAttemptAt = DateTime.UtcNow;
        ErrorMessage = Clamp(reason);
        DeliveryStatus = NotificationDeliveryStatus.Skipped;
        NextRetryAt = null;
    }

    /// <summary>
    /// Defer delivery until <paramref name="until"/> without consuming retry budget.
    /// </summary>
    public void MarkDeferred(DateTime until, string reason)
    {
        LastAttemptAt = DateTime.UtcNow;
        ErrorMessage = Clamp(reason);
        DeliveryStatus = NotificationDeliveryStatus.Pending;
        NextRetryAt = until;
    }

    /// <summary>Holds a delivery reason to what the column can store, without splitting a surrogate pair.</summary>
    private static string? Clamp(string? reason)
    {
        if (reason is null || reason.Length <= MaxErrorMessageLength)
            return reason;

        var cut = MaxErrorMessageLength;
        if (char.IsHighSurrogate(reason[cut - 1]))
            cut--;

        return reason[..cut];
    }
}
