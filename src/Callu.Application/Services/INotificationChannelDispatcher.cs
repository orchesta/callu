using Callu.Domain.Enums;
using Callu.Shared.Models.Notifications;

namespace Callu.Application.Services;

/// <summary>
/// Channel-specific notification dispatcher.
/// Each implementation handles dispatch + retry for a single channel (Email, SMS, Voice).
/// </summary>
public interface INotificationChannelDispatcher
{
    /// <summary>
    /// The notification channel this dispatcher handles
    /// </summary>
    NotificationType Channel { get; }

    /// <summary>Send an already-created, already-persisted notification via this channel; the delivery outcome is
    /// recorded on <paramref name="notification"/> in memory only, and the caller owns persistence.</summary>
    Task SendAsync(
        Domain.Entities.Notification notification,
        string? email,
        string? phoneNumber,
        NotificationPayload payload,
        string? incidentUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retry a previously failed notification via this channel
    /// </summary>
    Task RetryAsync(
        Domain.Entities.Notification notification,
        string? baseUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a test notification via this channel.
    /// userId + email/phone are passed explicitly to avoid Infrastructure dependency.
    /// </summary>
    Task<(bool Success, string Message)> SendTestAsync(
        string userId,
        string? email,
        string? phoneNumber,
        CancellationToken cancellationToken = default);
}
