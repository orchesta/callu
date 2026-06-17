namespace Callu.Domain.Enums;

/// <summary>
/// Tracks the delivery lifecycle of a notification
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>
    /// Notification created, waiting to be sent
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// Notification is currently being sent
    /// </summary>
    Sending = 1,
    
    /// <summary>
    /// Notification was delivered successfully
    /// </summary>
    Delivered = 2,
    
    /// <summary>
    /// Delivery failed (may retry)
    /// </summary>
    Failed = 3,
    
    /// <summary>
    /// Delivery is being retried
    /// </summary>
    Retrying = 4,
    
    /// <summary>
    /// Permanently failed after all retry attempts exhausted
    /// </summary>
    PermanentlyFailed = 5,

    /// <summary>
    /// Delivery was intentionally skipped (e.g. SMTP unconfigured, channel disabled by
    /// org settings). Distinct from Delivered so metrics aren't inflated and from
    /// Failed so the retry queue ignores it.
    /// </summary>
    Skipped = 6
}
