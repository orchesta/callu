using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Notifications;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>Dispatches notifications via the voice-call channel.</summary>
// Everything that decides WHETHER a phone rings lives in PhoneChannelDispatcher; what is left here is call-specific.
public class VoiceCallChannelDispatcher(
    IUserContactRepository userContacts,
    ICommunicationProviderRegistry providerRegistry,
    IIncidentRepository incidents,
    CalluMetrics metrics,
    ILogger<VoiceCallChannelDispatcher> logger,
    ICallLogRepository callLogs,
    IRecipientLanguageResolver? recipientLanguage = null,
    IVoiceCallCoalescingGuard? voiceCoalescingGuard = null)
    : PhoneChannelDispatcher(userContacts, providerRegistry, incidents, metrics, logger)
{
    /// <summary>
    /// A cooldown-deferred call is re-deferred at most this long; past the window the call
    /// is placed regardless of the cooldown — a late page beats a dropped one.
    /// </summary>
    private static readonly TimeSpan MaxDeferralWindow = TimeSpan.FromMinutes(30);

    /// <summary>The language this call is spoken in, never the language the incident is written in.</summary>
    private async Task<string?> ResolveLanguageAsync(string? userId, CancellationToken cancellationToken) =>
        recipientLanguage is null ? null : await recipientLanguage.ResolveAsync(userId, cancellationToken);

    public override NotificationType Channel => NotificationType.VoiceCall;

    protected override CommunicationCapability Capability => CommunicationCapability.VoiceCalls;

    protected override string ChannelLabel => "voice";

    protected override string NoProviderReason => Messages.Get("voice.noProvider");

    protected override string NoPhoneReason => Messages.Get("voice.phoneNotAvailable");

    protected override string NoProviderTestHint => "No Voice provider configured. Go to Communications to add one.";

    protected override string TestSentMessage(string phoneNumber) => $"Test voice call initiated to {phoneNumber}";

    /// <summary>A voice call goes out only while the incident is still worth waking somebody for.</summary>
    // Deliberately NOT "Open only": that would throw away a page queued on purpose after an ack (a responder pressed 2), and
    // nobody would be called at all — see IncidentPageContext.TakenOverAfterThePageWasQueued.
    protected override bool MayStillDeliver(IncidentPageContext context) =>
        !context.IsOver && !context.TakenOverAfterThePageWasQueued;

    /// <summary>Whether a call is already on record under this page's call id, which is the one this retry would dial.</summary>
    // A dial whose answer said nothing (a gateway timeout while the service was still rendering) may well
    // have placed the call. The call it leaves is the only proof either way, and once there is one the
    // call log's own chain owns every further attempt — so this page has nothing left to do.
    protected override Task<bool> AlreadyWentOutAsync(
        Domain.Entities.Notification notification, CancellationToken cancellationToken) =>
        callLogs.AnyForAttemptAsync(notification.Id, cancellationToken);

    /// <summary>
    /// Pushes a retry forward while the user is inside the voice-call cooldown; past the max window
    /// the call is placed rather than dropped.
    /// </summary>
    protected override async Task<bool> TryDeferRetryAsync(
        Domain.Entities.Notification notification, CancellationToken cancellationToken)
    {
        if (voiceCoalescingGuard is null) return false;
        if (DateTime.UtcNow - notification.CreatedAt > MaxDeferralWindow) return false;

        // includeInFlight: false — the sweep claims its whole batch as Sending up front, and counting
        // those would make siblings defer each other. The anchor is DELIVERED calls only.
        var deferUntil = await voiceCoalescingGuard.GetDeferralUntilAsync(
            notification.UserId, notification.IncidentId, includeInFlight: false, cancellationToken);

        if (deferUntil is null) return false;

        notification.MarkDeferred(deferUntil.Value, "voice cooldown: recent call to same user");
        return true;
    }

    protected override async Task<ProviderSendResult> DeliverFirstAsync(
        ICommunicationProvider provider,
        string phoneNumber,
        Domain.Entities.Notification notification,
        NotificationPayload payload,
        CancellationToken cancellationToken)
    {
        var result = await provider.MakeCallAsync(new MakeCallRequest
        {
            Destination = phoneNumber,
            IncidentId = payload.IncidentId,
            IncidentTitle = payload.Title,
            Severity = payload.Severity,
            Description = payload.Description,
            ServiceName = payload.ServiceName,
            // Two different languages: the prompts are spoken to this person in theirs, the incident
            // text in whatever it was written in.
            Language = await ResolveLanguageAsync(notification.UserId, cancellationToken),
            DataLanguage = payload.DataLanguage,
            AttemptId = notification.Id
        });

        return new ProviderSendResult(result.Success, result.ErrorMessage);
    }

    protected override async Task<ProviderSendResult> DeliverRetryAsync(
        ICommunicationProvider provider,
        string phoneNumber,
        Domain.Entities.Notification notification,
        CancellationToken cancellationToken)
    {
        // Carry the incident context so a deferred FIRST call (which goes out through this retry
        // path) still gets a proper spoken prompt; ordinary failure-retries benefit too.
        var result = await provider.MakeCallAsync(new MakeCallRequest
        {
            Destination = phoneNumber,
            IncidentId = notification.IncidentId,
            IncidentTitle = notification.Incident?.Title,
            Severity = notification.Incident?.Severity.ToString(),
            Description = notification.Incident?.Description,
            Language = await ResolveLanguageAsync(notification.UserId, cancellationToken),
            DataLanguage = notification.Incident?.DataLanguage,
            CustomData = $"retry:{notification.RetryCount}|incident:{notification.IncidentId}",
            AttemptId = notification.Id
        });

        return new ProviderSendResult(result.Success, result.ErrorMessage);
    }

    protected override async Task<ProviderSendResult> DeliverTestAsync(
        ICommunicationProvider provider,
        string userId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var result = await provider.MakeCallAsync(new MakeCallRequest
        {
            Destination = phoneNumber,
            Language = await ResolveLanguageAsync(userId, cancellationToken),
        });
        return new ProviderSendResult(result.Success, result.ErrorMessage);
    }
}
