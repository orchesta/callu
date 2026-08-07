using System.Diagnostics;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>Everything a voice call and an SMS do identically, written once.</summary>
// SendAsync/RetryAsync/SendTestAsync are deliberately NOT virtual: a subclass supplies only the per-channel pieces, so a fix
// landing on one channel and not the other becomes a compile error. The exception is MayStillDeliver — see it.
public abstract class PhoneChannelDispatcher : INotificationChannelDispatcher
{
    private readonly IUserContactRepository _userContacts;
    private readonly ICommunicationProviderRegistry _providerRegistry;
    private readonly IIncidentRepository _incidents;
    private readonly CalluMetrics _metrics;

    protected PhoneChannelDispatcher(
        IUserContactRepository userContacts,
        ICommunicationProviderRegistry providerRegistry,
        IIncidentRepository incidents,
        CalluMetrics metrics,
        ILogger logger)
    {
        _userContacts = userContacts;
        _providerRegistry = providerRegistry;
        _incidents = incidents;
        _metrics = metrics;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    /// <summary>How long a page whose provider is configured but not usable keeps being re-queued before it is written off.</summary>
    // Covers only the non-permanent states — a cold registry, a provider whose initialize threw, one an operator toggled off —
    // all of which clear on a reload. A channel with NO provider configured is answered immediately instead.
    internal static readonly TimeSpan ProviderUnavailableWindow = TimeSpan.FromMinutes(30);

    /// <summary>How long to wait before asking the registry again.</summary>
    // Short, because a warm-up or a failed initialize clears on the next reload and this page has reached nobody yet.
    internal static readonly TimeSpan ProviderUnavailableRetryDelay = TimeSpan.FromSeconds(60);

    public abstract NotificationType Channel { get; }

    /// <summary>Which capability to ask <see cref="ICommunicationProviderRegistry"/> for.</summary>
    protected abstract CommunicationCapability Capability { get; }

    /// <summary>The provider currently serving <see cref="Capability"/>, or null when none is usable.</summary>
    protected ICommunicationProvider? ActiveProvider => _providerRegistry.GetProvider(Capability);

    /// <summary>Metric/log label: "voice", "sms".</summary>
    protected abstract string ChannelLabel { get; }

    /// <summary>Recorded on the row when no provider for <see cref="Capability"/> is registered.</summary>
    protected abstract string NoProviderReason { get; }

    /// <summary>Recorded on the row when the user has no phone number.</summary>
    protected abstract string NoPhoneReason { get; }

    /// <summary>What the "send test notification" button says when the channel has no provider.</summary>
    protected abstract string NoProviderTestHint { get; }

    /// <summary>What the "send test notification" button says when it worked.</summary>
    protected abstract string TestSentMessage(string phoneNumber);

    /// <summary>The provider call for a first dispatch (it has the full incident payload).</summary>
    protected abstract Task<ProviderSendResult> DeliverFirstAsync(
        ICommunicationProvider provider,
        string phoneNumber,
        Domain.Entities.Notification notification,
        NotificationPayload payload,
        CancellationToken cancellationToken);

    /// <summary>The provider call for a retry (it only has the persisted row + its incident).</summary>
    protected abstract Task<ProviderSendResult> DeliverRetryAsync(
        ICommunicationProvider provider,
        string phoneNumber,
        Domain.Entities.Notification notification,
        CancellationToken cancellationToken);

    /// <summary>The provider call behind the "send test notification" button.</summary>
    protected abstract Task<ProviderSendResult> DeliverTestAsync(
        ICommunicationProvider provider,
        string userId,
        string phoneNumber,
        CancellationToken cancellationToken);

    /// <summary>Whether a page queued for this incident is still worth delivering on THIS channel.</summary>
    // The one decision the two channels must NOT share: a voice call rings a human who may already own the incident, while an
    // SMS wakes nobody and suppressing it means a responder whose gateway blipped never learns the incident happened.
    protected abstract bool MayStillDeliver(IncidentPageContext context);

    /// <summary>Channel-specific gate on the retry path, run before any provider work.</summary>
    // True means the row's outcome is already recorded and nothing should be sent; the voice-call cooldown is the only user.
    protected virtual Task<bool> TryDeferRetryAsync(
        Domain.Entities.Notification notification, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>Whether a delivery for this row has already reached the recipient, so re-sending would be a second one.</summary>
    // Only a channel that leaves a durable record of what it delivered can answer this; the default is
    // "cannot tell", which keeps the retry exactly where it was.
    protected virtual Task<bool> AlreadyWentOutAsync(
        Domain.Entities.Notification notification, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>
    /// FIRST dispatch. The row is already claimed and persisted; the outcome is recorded on it in
    /// memory and the caller (<see cref="NotificationDispatcher"/>) commits it.
    /// </summary>
    public async Task SendAsync(
        Domain.Entities.Notification notification,
        string? email,
        string? phoneNumber,
        NotificationPayload payload,
        string? incidentUrl,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var provider = _providerRegistry.GetProvider(Capability);
            if (provider is null)
            {
                MarkProviderUnavailable(notification);
            }
            else if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                // NotificationDispatcher only creates a phone-channel row for a user who HAS a number,
                // so this is belt-and-braces; a missing number really is permanent, though.
                notification.MarkPermanentlyFailed(NoPhoneReason);
                Logger.LogWarning("[NOTIFICATION] User {UserId} has no phone number; {Channel} cannot be delivered",
                    notification.UserId, ChannelLabel);
            }
            else
            {
                var result = await DeliverFirstAsync(provider, phoneNumber, notification, payload, cancellationToken);
                RecordProviderAnswer(notification, result, phoneNumber, payload.IncidentId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The provider was asked and blew up. That IS an answer of a kind — the retry queue owns
            // the row from here, with its own bounded attempt budget.
            // A throw says nothing about whether the far end acted on the request, so the next attempt
            // is held back further than a refusal's: re-sending at once is what rings a phone twice.
            notification.MarkUndetermined(ex.Message);
            Logger.LogError(ex, "[NOTIFICATION] Error delivering {Channel} to {Phone}; whether it went out is unknown",
                ChannelLabel, phoneNumber);
        }

        NotificationDispatchMetrics.RecordAfterDispatch(_metrics, sw, ChannelLabel, notification);
    }

    /// <summary>
    /// RE-dispatch, from the notification retry sweep. Everything here is ordered so that the
    /// cheapest way to NOT ring a phone comes first.
    /// </summary>
    public async Task RetryAsync(
        Domain.Entities.Notification notification,
        string? baseUrl,
        CancellationToken cancellationToken = default)
    {
        // 1. Is this page still wanted? Re-read from the database, never from the entity the sweep loaded: a batch of fifty
        //    sends serially outside any transaction, so by this row's turn the claim query's copy may be minutes stale.
        if (!await StillWantedAsync(notification, cancellationToken))
            return;

        // 2. Did the attempt this row records already reach the recipient? An answer the provider could
        //    not give at the time does not stop the delivery happening, and re-sending on that gap is a
        //    second delivery of the same page.
        if (await AlreadyWentOutAsync(notification, cancellationToken))
        {
            notification.MarkDelivered();
            Logger.LogInformation(
                "[RETRY] A {Channel} delivery for user {UserId} on incident {IncidentId} is already on record; "
                + "not sending a second one",
                ChannelLabel, notification.UserId, notification.IncidentId);
            return;
        }

        // 3. Channel-specific hold (the voice-call cooldown).
        if (await TryDeferRetryAsync(notification, cancellationToken))
            return;

        var contact = await _userContacts.GetContactByIdAsync(notification.UserId, cancellationToken);
        var phoneNumber = contact?.PhoneNumber;
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            notification.MarkPermanentlyFailed(NoPhoneReason);
            return;
        }

        // 4. NO PROVIDER IS NOT A REFUSAL — but it is not automatically a wait, either. Which of the
        //    two it is depends on WHY the registry is empty-handed; MarkProviderUnavailable asks.
        var provider = _providerRegistry.GetProvider(Capability);
        if (provider is null)
        {
            MarkProviderUnavailable(notification);
            return;
        }

        try
        {
            var result = await DeliverRetryAsync(provider, phoneNumber, notification, cancellationToken);

            if (result.Success)
            {
                notification.MarkDelivered();
                Logger.LogInformation("[RETRY] {Channel} to {Phone} on attempt {Attempt}",
                    ChannelLabel, phoneNumber, notification.RetryCount);
            }
            else
            {
                notification.MarkFailed(
                    $"{ChannelLabel} retry attempt {notification.RetryCount + 1}: {result.ErrorMessage}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.MarkUndetermined(
                $"{ChannelLabel} retry attempt {notification.RetryCount + 1}: {ex.Message}");
            Logger.LogError(ex, "[RETRY] Unexpected error delivering {Channel} to {Phone}; whether it went out is unknown",
                ChannelLabel, phoneNumber);
        }
    }

    public async Task<(bool Success, string Message)> SendTestAsync(
        string userId,
        string? email,
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return (false, "No phone number configured on your profile");

        var provider = _providerRegistry.GetProvider(Capability);
        if (provider is null)
            return (false, NoProviderTestHint);

        var result = await DeliverTestAsync(provider, userId, phoneNumber, cancellationToken);

        return result.Success
            ? (true, TestSentMessage(phoneNumber))
            : (false, $"{ChannelLabel} test failed: {result.ErrorMessage}");
    }

    private void RecordProviderAnswer(
        Domain.Entities.Notification notification,
        ProviderSendResult result,
        string phoneNumber,
        Guid? incidentId)
    {
        if (result.Success)
        {
            notification.MarkDelivered();
            Logger.LogInformation("[NOTIFICATION] {Channel} sent to {Phone} for incident {IncidentId}",
                ChannelLabel, phoneNumber, incidentId);
            return;
        }

        notification.MarkFailed(result.ErrorMessage ?? $"{ChannelLabel} delivery failed");
        Logger.LogWarning("[NOTIFICATION] {Channel} failed to {Phone}: {Error}",
            ChannelLabel, phoneNumber, result.ErrorMessage);
    }

    /// <summary>Records the outcome when the registry has no usable provider for this channel.</summary>
    // WHY there is none decides everything: NotConfigured is a completed load that found nothing, so the row is Skipped at once
    // and escalation stops waiting; a cold registry or a toggled-off provider WAITS, on wall-clock, without spending retry budget.
    private void MarkProviderUnavailable(Domain.Entities.Notification notification)
    {
        var absence = _providerRegistry.DescribeAbsence(Capability);

        if (absence == ProviderAbsence.NotConfigured)
        {
            notification.MarkSkipped(NoProviderReason);
            Logger.LogError(
                "[NOTIFICATION] No {Channel} provider is configured on this installation, so the page to user {UserId} "
                + "on incident {IncidentId} was NOT sent and never will be — the registry has completed a load and "
                + "there is no provider for this channel to find. This responder was NOT contacted on {Channel}; "
                + "configure a provider for it (Settings → Communications).",
                ChannelLabel, notification.UserId, notification.IncidentId, ChannelLabel);
            return;
        }

        var now = DateTime.UtcNow;

        // CreatedAt is stamped when the row is CLAIMED, which always happens before a dispatcher sees
        // it. A row that somehow carries no stamp reads as brand new — the safe direction is to keep
        // trying, never to give up on a page nobody has attempted.
        var createdAt = notification.CreatedAt == default ? now : notification.CreatedAt;
        var waitingFor = now - createdAt;

        if (waitingFor >= ProviderUnavailableWindow)
        {
            notification.MarkSkipped(NoProviderReason);
            Logger.LogError(
                "[NOTIFICATION] The {Channel} provider has been unusable for {Waiting:g} ({Absence}) for the page to "
                + "user {UserId} on incident {IncidentId}. Giving up: this page was NEVER ATTEMPTED and nobody was "
                + "reached on this channel — check the provider's configuration (Settings → Communications).",
                ChannelLabel, waitingFor, absence, notification.UserId, notification.IncidentId);
            return;
        }

        notification.MarkDeferred(now.Add(ProviderUnavailableRetryDelay), NoProviderReason);
        Logger.LogWarning(
            "[NOTIFICATION] No usable {Channel} provider for user {UserId} on incident {IncidentId} ({Absence}); the "
            + "send was NOT attempted. Re-queued for {Delay:g} — this state clears on a registry reload; giving up "
            + "after {Window:g}.",
            ChannelLabel, notification.UserId, notification.IncidentId, absence, ProviderUnavailableRetryDelay,
            ProviderUnavailableWindow);
    }

    /// <summary>Whether the incident behind this page is still one we should be contacting people about.</summary>
    // Answered from a fresh scalar read, never the loaded entity. A page with no incident behind it is always still wanted.
    private async Task<bool> StillWantedAsync(
        Domain.Entities.Notification notification, CancellationToken cancellationToken)
    {
        if (notification.IncidentId is not { } incidentId) return true;

        var incident = await _incidents.GetQueryable()
            .AsNoTracking()
            .Where(i => i.Id == incidentId)
            .Select(i => new { i.Status, i.AcknowledgedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (incident is null)
        {
            notification.MarkSkipped("the incident no longer exists");
            Logger.LogInformation(
                "[RETRY] Incident {IncidentId} is gone; the pending {Channel} page to user {UserId} stands down",
                incidentId, ChannelLabel, notification.UserId);
            return false;
        }

        var context = new IncidentPageContext(
            incident.Status, incident.AcknowledgedAt, notification.CreatedAt);

        if (MayStillDeliver(context)) return true;

        notification.MarkSkipped($"incident is {incident.Status} — no longer contacting responders");
        Logger.LogInformation(
            "[RETRY] Incident {IncidentId} is {Status}; the pending {Channel} page to user {UserId} stands down",
            incidentId, incident.Status, ChannelLabel, notification.UserId);
        return false;
    }
}

/// <summary>What a provider answered when it was asked to deliver. Success, or a reason.</summary>
public readonly record struct ProviderSendResult(bool Success, string? ErrorMessage);

/// <summary>Everything <see cref="PhoneChannelDispatcher.MayStillDeliver"/> may decide on, read fresh at the moment of the retry.</summary>
// AcknowledgedAt is stamped by both Acknowledge and StartInvestigation, so every route out of Open into "somebody owns this" sets it.
public readonly record struct IncidentPageContext(
    IncidentStatus Status,
    DateTime? AcknowledgedAt,
    DateTime PageCreatedAt)
{
    /// <summary>Whether a human took this incident over AFTER this page was queued, making the page stale news.</summary>
    // The comparison against PageCreatedAt is the point: a page deliberately queued after an ack (a responder pressed 2) must
    // still be retried. A missing timestamp cannot prove the takeover came later, and the safe direction there is to deliver.
    public bool TakenOverAfterThePageWasQueued =>
        Status != IncidentStatus.Open &&
        AcknowledgedAt is { } takenOverAt &&
        takenOverAt > PageCreatedAt;

    /// <summary>Resolved or Closed: it is over, for every channel.</summary>
    public bool IsOver => Status.IsTerminal();
}
