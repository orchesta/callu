using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Source-generated high-performance log messages for NotificationDispatcher.
/// </summary>
internal static partial class NotificationDispatcherLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Dispatching notification via {Channel} to user {UserId} for incident {IncidentId}")]
    public static partial void DispatchingNotification(ILogger logger, string channel, string userId, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "No on-call user found for schedule {ScheduleId}")]
    public static partial void NoOnCallUserFound(ILogger logger, Guid scheduleId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Processing {Count} failed notifications for retry")]
    public static partial void ProcessingFailedNotifications(ILogger logger, int count);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "[RETRY] Failed notification {NotificationId} (attempt {Attempt})")]
    public static partial void RetryFailed(ILogger logger, Exception ex, Guid notificationId, int attempt);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to push real-time notification to user {UserId}")]
    public static partial void RealtimePushFailed(ILogger logger, Exception ex, string userId);
}
