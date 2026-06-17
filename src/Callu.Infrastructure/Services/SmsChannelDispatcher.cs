using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Notifications;
using Callu.Shared.Localization;
using Callu.Infrastructure.Telemetry;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Dispatches notifications via SMS channel — handles both initial dispatch and retries.
/// </summary>
public class SmsChannelDispatcher(
    INotificationRepository notificationRepo,
    IUserContactRepository userContacts,
    ICommunicationProviderRegistry providerRegistry,
    CalluMetrics metrics,
    ILogger<SmsChannelDispatcher> logger) : INotificationChannelDispatcher
{
    public NotificationType Channel => NotificationType.Sms;

    public async Task<Domain.Entities.Notification> DispatchAsync(
        string userId,
        string? email,
        string? phoneNumber,
        NotificationPayload payload,
        string? incidentUrl,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var notification = NotificationFactory.Create(userId, payload, incidentUrl, NotificationType.Sms);
        notification.MarkSending();

        try
        {
            var smsProvider = providerRegistry.GetProvider(CommunicationCapability.Sms);
            if (smsProvider == null)
            {
                notification.MarkSkipped("No SMS provider configured");
                logger.LogWarning("[NOTIFICATION] No SMS provider available for user {UserId}", userId);
            }
            else
            {
                var smsMessage = $"[{payload.Severity}] {payload.Title}";
                if (payload.Description != null)
                {
                    smsMessage += $" — {payload.Description}";
                }

                if (smsMessage.Length > 160)
                {
                    smsMessage = smsMessage[..157] + "...";
                }

                var result = await smsProvider.SendSmsAsync(new SendSmsRequest
                {
                    To = phoneNumber!,
                    Message = smsMessage
                });

                if (result.Success)
                {
                    notification.MarkDelivered();
                    logger.LogInformation("[NOTIFICATION] SMS sent to {Phone} for incident {IncidentId}",
                        phoneNumber, payload.IncidentId);
                }
                else
                {
                    notification.MarkFailed(result.ErrorMessage ?? "SMS send failed");
                    logger.LogWarning("[NOTIFICATION] SMS failed to {Phone}: {Error}",
                        phoneNumber, result.ErrorMessage);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            notification.MarkFailed(ex.Message);
            logger.LogError(ex, "[NOTIFICATION] Error sending SMS to {Phone}", phoneNumber);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.MarkFailed(ex.Message);
            logger.LogError(ex, "[NOTIFICATION] Unexpected error sending SMS to {Phone}", phoneNumber);
        }

        await notificationRepo.AddAsync(notification, cancellationToken);
        NotificationDispatchMetrics.RecordAfterDispatch(metrics, sw, "sms", notification);
        return notification;
    }

    public async Task RetryAsync(
        Domain.Entities.Notification notification,
        string? baseUrl,
        CancellationToken cancellationToken = default)
    {
        var contact = await userContacts.GetContactByIdAsync(notification.UserId, cancellationToken);
        var phoneNumber = contact?.PhoneNumber;
        if (string.IsNullOrEmpty(phoneNumber))
        {
            notification.MarkPermanentlyFailed(Messages.Get("sms.phoneNotAvailable"));
            return;
        }

        var smsProvider = providerRegistry.GetProvider(CommunicationCapability.Sms);
        if (smsProvider == null)
        {
            notification.MarkPermanentlyFailed(Messages.Get("sms.noProvider"));
            return;
        }

        try
        {
            var result = await smsProvider.SendSmsAsync(new SendSmsRequest
            {
                To = phoneNumber,
                Message = notification.Message ?? notification.Title
            });

            if (result.Success)
            {
                notification.MarkDelivered();
                logger.LogInformation("[RETRY] SMS sent to {Phone} on attempt {Attempt}", phoneNumber, notification.RetryCount);
            }
            else
            {
                notification.MarkFailed($"SMS retry attempt {notification.RetryCount + 1}: {result.ErrorMessage}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.MarkFailed($"SMS retry attempt {notification.RetryCount + 1}: {ex.Message}");
            logger.LogError(ex, "[RETRY] Unexpected error sending SMS to {Phone}", phoneNumber);
        }
    }

    public Task<(bool Success, string Message)> SendTestAsync(
        string userId,
        string? email,
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        return SendTestInternalAsync(phoneNumber, cancellationToken);
    }

    private async Task<(bool Success, string Message)> SendTestInternalAsync(
        string? phoneNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(phoneNumber))
            return (false, "No phone number configured on your profile");

        var smsProvider = providerRegistry.GetProvider(CommunicationCapability.Sms);
        if (smsProvider == null)
            return (false, "No SMS provider configured. Go to Communications to add one.");

        var result = await smsProvider.SendSmsAsync(new SendSmsRequest
        {
            To = phoneNumber,
            Message = Messages.Get("sms.testMessage")
        });

        return result.Success
            ? (true, $"Test SMS sent to {phoneNumber}")
            : (false, $"SMS send failed: {result.ErrorMessage}");
    }
}
