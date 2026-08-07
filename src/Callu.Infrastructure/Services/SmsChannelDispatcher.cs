using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Domain.Enums;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Notifications;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>Dispatches notifications via the SMS channel.</summary>
// Everything that decides whether a message goes out lives in PhoneChannelDispatcher, shared with the voice channel.
public class SmsChannelDispatcher(
    IUserContactRepository userContacts,
    ICommunicationProviderRegistry providerRegistry,
    IIncidentRepository incidents,
    CalluMetrics metrics,
    ILogger<SmsChannelDispatcher> logger)
    : PhoneChannelDispatcher(userContacts, providerRegistry, incidents, metrics, logger)
{
    /// <summary>One GSM segment, minus the ellipsis a truncated message ends with.</summary>
    private const int MaxSmsLength = 160;

    public override NotificationType Channel => NotificationType.Sms;

    protected override CommunicationCapability Capability => CommunicationCapability.Sms;

    protected override string ChannelLabel => "sms";

    protected override string NoProviderReason => Messages.Get("sms.noProvider");

    protected override string NoPhoneReason => Messages.Get("sms.phoneNotAvailable");

    protected override string NoProviderTestHint => "No SMS provider configured. Go to Communications to add one.";

    protected override string TestSentMessage(string phoneNumber) => $"Test SMS sent to {phoneNumber}";

    /// <summary>An SMS stops when the incident is OVER, and not before.</summary>
    // Deliberately not the voice rule: an SMS rings nobody, so stopping it on a taken-over incident means a responder whose
    // gateway blipped never learns the incident happened. Resolved/Closed is different — there is nothing left to tell.
    protected override bool MayStillDeliver(IncidentPageContext context) => !context.IsOver;

    protected override async Task<ProviderSendResult> DeliverFirstAsync(
        ICommunicationProvider provider,
        string phoneNumber,
        Domain.Entities.Notification notification,
        NotificationPayload payload,
        CancellationToken cancellationToken)
    {
        var message = $"[{payload.Severity}] {payload.Title}";
        if (payload.Description != null)
            message += $" — {payload.Description}";

        if (message.Length > MaxSmsLength)
            message = message[..(MaxSmsLength - 3)] + "...";

        var result = await provider.SendSmsAsync(new SendSmsRequest
        {
            To = phoneNumber,
            Message = message
        });

        return new ProviderSendResult(result.Success, result.ErrorMessage);
    }

    protected override async Task<ProviderSendResult> DeliverRetryAsync(
        ICommunicationProvider provider,
        string phoneNumber,
        Domain.Entities.Notification notification,
        CancellationToken cancellationToken)
    {
        var result = await provider.SendSmsAsync(new SendSmsRequest
        {
            To = phoneNumber,
            Message = notification.Message ?? notification.Title
        });

        return new ProviderSendResult(result.Success, result.ErrorMessage);
    }

    protected override async Task<ProviderSendResult> DeliverTestAsync(
        ICommunicationProvider provider,
        string userId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var result = await provider.SendSmsAsync(new SendSmsRequest
        {
            To = phoneNumber,
            Message = Messages.Get("sms.testMessage")
        });

        return new ProviderSendResult(result.Success, result.ErrorMessage);
    }
}
