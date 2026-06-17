using System.Diagnostics;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Telemetry;

internal static class NotificationDispatchMetrics
{
    public static void RecordAfterDispatch(
        CalluMetrics metrics,
        Stopwatch stopwatch,
        string channel,
        Notification notification)
    {
        metrics.RecordNotificationLatency(stopwatch.Elapsed.TotalMilliseconds, channel);

        if (notification.DeliveryStatus == NotificationDeliveryStatus.Delivered)
            metrics.NotificationSent(channel);
        else if (notification.DeliveryStatus is NotificationDeliveryStatus.Failed
                 or NotificationDeliveryStatus.PermanentlyFailed)
            metrics.NotificationFailed(channel, ClassifyFailure(notification));
    }

    /// <summary>
    /// Maps a failed notification to a small, bounded failure category for use as a metric tag.
    /// The raw (potentially PII-bearing, high-cardinality) <see cref="Notification.ErrorMessage"/>
    /// stays only on the persisted row — it must never become a metric label.
    /// Returns one of: permanent, auth, rate-limited, timeout, config, transient, unknown.
    /// </summary>
    internal static string ClassifyFailure(Notification notification)
    {
        if (notification.DeliveryStatus == NotificationDeliveryStatus.PermanentlyFailed)
            return "permanent";

        var message = notification.ErrorMessage;
        if (string.IsNullOrWhiteSpace(message))
            return "unknown";

        return message switch
        {
            _ when Contains(message, "401") || Contains(message, "unauthorized") || Contains(message, "forbidden") || Contains(message, "403") => "auth",
            _ when Contains(message, "429") || Contains(message, "rate") || Contains(message, "throttle") => "rate-limited",
            _ when Contains(message, "timeout") || Contains(message, "timed out") => "timeout",
            _ when Contains(message, "not configured") || Contains(message, "unconfigured") || Contains(message, "disabled") => "config",
            _ => "transient",
        };

        static bool Contains(string source, string value)
            => source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
