using System.Diagnostics;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Notifications;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Dispatches notifications via Email channel — handles both initial dispatch and retries.
/// </summary>
public class EmailChannelDispatcher(
    INotificationRepository notificationRepo,
    IEmailService emailService,
    IUserContactRepository userContacts,
    CalluMetrics metrics,
    ILogger<EmailChannelDispatcher> logger) : INotificationChannelDispatcher
{
    public NotificationType Channel => NotificationType.Email;

    public async Task<Domain.Entities.Notification> DispatchAsync(
        string userId,
        string? email,
        string? phoneNumber,
        NotificationPayload payload,
        string? incidentUrl,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var notification = NotificationFactory.Create(userId, payload, incidentUrl, NotificationType.Email);
        notification.MarkSending();

        try
        {
            var emailConfigured = await emailService.IsConfiguredAsync(cancellationToken);
            if (!emailConfigured)
            {
                notification.MarkSkipped("SMTP not configured");
                logger.LogInformation("[NOTIFICATION] Skipped (no SMTP config): {UserId} | {Title}",
                    userId, payload.Title);
            }
            else if (string.IsNullOrEmpty(email))
            {
                notification.MarkPermanentlyFailed("User has no email address");
                logger.LogWarning("[NOTIFICATION] User {UserId} has no email address; cannot deliver", userId);
            }
            else
            {
                var sent = await emailService.SendOnCallNotificationAsync(
                    email,
                    payload.Title ?? "Incident Alert",
                    payload.Severity ?? "Medium",
                    incidentUrl ?? "#",
                    cancellationToken);

                if (sent)
                {
                    notification.MarkDelivered();
                    logger.LogInformation("[NOTIFICATION] Email sent to {UserEmail} for incident {IncidentId}",
                        email, payload.IncidentId);
                }
                else
                {
                    notification.MarkFailed("Email service returned false");
                    logger.LogWarning("[NOTIFICATION] Failed to send email to {UserEmail}", email);
                }
            }
        }
        catch (SmtpException ex)
        {
            notification.MarkFailed(ex.Message);
            logger.LogError(ex, "[NOTIFICATION] Error sending email to {UserEmail}", email);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.MarkFailed(ex.Message);
            logger.LogError(ex, "[NOTIFICATION] Unexpected error sending email to {UserEmail}", email);
        }

        await notificationRepo.AddAsync(notification, cancellationToken);
        NotificationDispatchMetrics.RecordAfterDispatch(metrics, sw, "email", notification);
        return notification;
    }

    public async Task RetryAsync(
        Domain.Entities.Notification notification,
        string? baseUrl,
        CancellationToken cancellationToken = default)
    {
        var contact = await userContacts.GetContactByIdAsync(notification.UserId, cancellationToken);
        if (contact is null || string.IsNullOrEmpty(contact.Email))
        {
            notification.MarkPermanentlyFailed("User or email not found");
            return;
        }

        var incidentUrl = notification.ActionUrl ?? $"{baseUrl}/incidents/{notification.IncidentId}";
        var severity = notification.Incident?.Severity.ToString() ?? "Medium";

        var sent = await emailService.SendOnCallNotificationAsync(
            contact.Email, notification.Title, severity, incidentUrl, cancellationToken);

        if (sent)
        {
            notification.MarkDelivered();
            logger.LogInformation("[RETRY] Email sent to {Email} on attempt {Attempt}", contact.Email, notification.RetryCount);
        }
        else
        {
            notification.MarkFailed($"Email retry attempt {notification.RetryCount + 1} failed");
        }
    }

    public async Task<(bool Success, string Message)> SendTestAsync(
        string userId,
        string? email,
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(email))
            return (false, "No email address configured");

        var emailConfigured = await emailService.IsConfiguredAsync(cancellationToken);
        if (!emailConfigured)
            return (false, "SMTP is not configured. Go to Settings to configure email.");

        var sent = await emailService.SendOnCallNotificationAsync(
            email, "Test Notification", "Medium", "#", cancellationToken);

        return sent
            ? (true, $"Test email sent to {email}")
            : (false, "Email service failed to send");
    }
}
