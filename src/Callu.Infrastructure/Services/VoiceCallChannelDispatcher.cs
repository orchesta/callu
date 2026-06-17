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
/// Dispatches notifications via Voice Call channel — handles both initial dispatch and retries.
/// </summary>
public class VoiceCallChannelDispatcher(
    INotificationRepository notificationRepo,
    IUserContactRepository userContacts,
    ICommunicationProviderRegistry providerRegistry,
    CalluMetrics metrics,
    ILogger<VoiceCallChannelDispatcher> logger) : INotificationChannelDispatcher
{
    public NotificationType Channel => NotificationType.VoiceCall;

    public async Task<Domain.Entities.Notification> DispatchAsync(
        string userId,
        string? email,
        string? phoneNumber,
        NotificationPayload payload,
        string? incidentUrl,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var notification = NotificationFactory.Create(userId, payload, incidentUrl, NotificationType.VoiceCall);
        notification.MarkSending();

        try
        {
            var voiceProvider = providerRegistry.GetProvider(CommunicationCapability.VoiceCalls);
            if (voiceProvider == null)
            {
                notification.MarkSkipped("No voice call provider configured");
                logger.LogWarning("[NOTIFICATION] No voice call provider available for user {UserId}", userId);
            }
            else
            {
                var result = await voiceProvider.MakeCallAsync(new MakeCallRequest
                {
                    Destination = phoneNumber!,
                    IncidentId = payload.IncidentId,
                    IncidentTitle = payload.Title,
                    Severity = payload.Severity,
                    Description = payload.Description,
                    ServiceName = payload.ServiceName,
                    DataLanguage = payload.DataLanguage
                });

                if (result.Success)
                {
                    notification.MarkDelivered();
                    logger.LogInformation("[NOTIFICATION] Voice call initiated to {Phone} for incident {IncidentId}",
                        phoneNumber, payload.IncidentId);
                }
                else
                {
                    notification.MarkFailed(result.ErrorMessage ?? "Voice call failed");
                    logger.LogWarning("[NOTIFICATION] Voice call failed to {Phone}: {Error}",
                        phoneNumber, result.ErrorMessage);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            notification.MarkFailed(ex.Message);
            logger.LogError(ex, "[NOTIFICATION] Error making voice call to {Phone}", phoneNumber);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.MarkFailed(ex.Message);
            logger.LogError(ex, "[NOTIFICATION] Unexpected error making voice call to {Phone}", phoneNumber);
        }

        await notificationRepo.AddAsync(notification, cancellationToken);
        NotificationDispatchMetrics.RecordAfterDispatch(metrics, sw, "voice", notification);
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
            notification.MarkPermanentlyFailed(Messages.Get("voice.phoneNotAvailable"));
            return;
        }

        var voiceProvider = providerRegistry.GetProvider(CommunicationCapability.VoiceCalls);
        if (voiceProvider == null)
        {
            notification.MarkPermanentlyFailed(Messages.Get("voice.noProvider"));
            return;
        }

        try
        {
            var result = await voiceProvider.MakeCallAsync(new MakeCallRequest
            {
                Destination = phoneNumber,
                CustomData = $"retry:{notification.RetryCount}|incident:{notification.IncidentId}"
            });

            if (result.Success)
            {
                notification.MarkDelivered();
                logger.LogInformation("[RETRY] Voice call to {Phone} on attempt {Attempt}", phoneNumber, notification.RetryCount);
            }
            else
            {
                notification.MarkFailed($"Voice call retry attempt {notification.RetryCount + 1}: {result.ErrorMessage}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.MarkFailed($"Voice call retry attempt {notification.RetryCount + 1}: {ex.Message}");
            logger.LogError(ex, "[RETRY] Unexpected error making voice call to {Phone}", phoneNumber);
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

        var voiceProvider = providerRegistry.GetProvider(CommunicationCapability.VoiceCalls);
        if (voiceProvider == null)
            return (false, "No Voice provider configured. Go to Communications to add one.");

        var result = await voiceProvider.MakeCallAsync(new MakeCallRequest
        {
            Destination = phoneNumber
        });

        return result.Success
            ? (true, $"Test voice call initiated to {phoneNumber}")
            : (false, $"Voice call failed: {result.ErrorMessage}");
    }
}
