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

    /// <summary>
    /// Dispatch a new notification via this channel.
    /// Returns the created Notification entity (already persisted).
    /// </summary>
    Task<Domain.Entities.Notification> DispatchAsync(
        string userId,
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
