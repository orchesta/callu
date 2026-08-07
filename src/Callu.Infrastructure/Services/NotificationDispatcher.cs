using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Shared.Models.Notifications;
using Callu.Shared.Models.Schedules;

namespace Callu.Infrastructure.Services;

/// <summary>Routes notifications to channel dispatchers, applying per-user preferences, quiet hours, on-call lookup and the retry queue.</summary>
public class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationRepository _notificationRepo;
    private readonly INotificationPreferenceRepository _prefRepo;
    private readonly ITeamMemberRepository _teamMemberRepo;
    private readonly ITransactionManager _transactionManager;
    private readonly IUserContactRepository _userContacts;
    private readonly IOnCallService _onCallService;
    private readonly IOrganizationSettingsService _organizationSettingsService;
    private readonly INotificationPushService? _pushService;
    private readonly IDateTimeZoneProvider _tzProvider;
    private readonly IClock _clock;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly Dictionary<NotificationType, INotificationChannelDispatcher> _channelDispatchers;
    private readonly IVoiceCallCoalescingGuard? _voiceCoalescingGuard;

    public NotificationDispatcher(
        INotificationRepository notificationRepo,
        INotificationPreferenceRepository prefRepo,
        ITeamMemberRepository teamMemberRepo,
        ITransactionManager transactionManager,
        IUserContactRepository userContacts,
        IOnCallService onCallService,
        IOrganizationSettingsService organizationSettingsService,
        IDateTimeZoneProvider tzProvider,
        IClock clock,
        IEnumerable<INotificationChannelDispatcher> channelDispatchers,
        ApplicationDbContext dbContext,
        ILogger<NotificationDispatcher> logger,
        INotificationPushService? pushService = null,
        IVoiceCallCoalescingGuard? voiceCoalescingGuard = null)
    {
        _logger = logger;
        _notificationRepo = notificationRepo;
        _prefRepo = prefRepo;
        _teamMemberRepo = teamMemberRepo;
        _transactionManager = transactionManager;
        _userContacts = userContacts;
        _onCallService = onCallService;
        _organizationSettingsService = organizationSettingsService;
        _tzProvider = tzProvider;
        _clock = clock;
        _dbContext = dbContext;
        _pushService = pushService;
        _voiceCoalescingGuard = voiceCoalescingGuard;
        _channelDispatchers = channelDispatchers.ToDictionary(d => d.Channel);
    }

    /// <summary>How long one channel may hold a dispatch pass before its remaining rows are left to the next sweep.</summary>
    // Providers are asked one row at a time, and a self-hosted voice service renders every prompt before it
    // answers — without this, one wedged provider is fifty minutes in which no e-mail or SMS retry moves.
    internal static readonly TimeSpan PerChannelDispatchBudget = TimeSpan.FromSeconds(60);

    /// <summary>How long each channel has spent inside one dispatch pass.</summary>
    private sealed class ChannelDispatchBudget(IClock clock, TimeSpan budget)
    {
        private readonly Dictionary<NotificationType, Duration> _spent = [];

        public DateTime Now() => clock.GetCurrentInstant().ToDateTimeUtc();

        public Instant StartOfAttempt() => clock.GetCurrentInstant();

        public bool IsSpent(NotificationType channel) =>
            _spent.TryGetValue(channel, out var spent) && spent >= Duration.FromTimeSpan(budget);

        public void ChargeSince(NotificationType channel, Instant startedAt) =>
            _spent[channel] = _spent.GetValueOrDefault(channel) + (clock.GetCurrentInstant() - startedAt);

        /// <summary>What is written on a row this pass ran out of time for.</summary>
        public string Exhausted(NotificationType channel) =>
            $"the {channel} provider used the whole {budget.TotalSeconds:0}s this dispatch pass allows it; "
            + "this page is queued and the retry sweep will send it";
    }

    public async Task<NotificationDispatchResult> NotifyUsersAsync(IEnumerable<string> userIds, NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        var baseUrl = await _organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);
        var incidentUrl = $"{baseUrl}/incidents/{payload.IncidentId}";
        var budget = new ChannelDispatchBudget(_clock, PerChannelDispatchBudget);

        var reached = 0;
        var failed = 0;
        var silent = 0;
        var unpageable = 0;
        var channelFailures = new List<DispatchChannelFailure>();
        var channelSilences = new List<DispatchChannelSilence>();
        var channelDeferrals = new List<DispatchChannelDeferral>();
        var unpageableTargets = new List<DispatchUnpageableTarget>();
        var optedOutChannels = new List<DispatchChannelOptedOut>();

        foreach (var userId in userIds.Distinct(StringComparer.Ordinal))
        {
            var contact = await _userContacts.GetContactByIdAsync(userId, cancellationToken);
            if (contact is null)
            {
                // THE ROTA POINTS AT AN ACCOUNT WE CANNOT CONTACT — somebody IS on the rota (an empty one returns
                // NobodyToPage before reaching here) but has no contact record. Its own record, not "empty rota".
                unpageable++;
                unpageableTargets.Add(new DispatchUnpageableTarget(
                    userId,
                    "the rota points to this account, but it has no contact record — it may have been "
                    + "deleted or was never fully provisioned, so nothing can be sent to it"));
                _logger.LogError(
                    "User {UserId} is on the rota for incident {IncidentId} but has NO contact record; the "
                    + "rota still points to them. This is not an empty rota and not a broken channel — the "
                    + "account/contact profile is the fault.",
                    userId, payload.IncidentId);
                continue;
            }

            // Through the repository, so the paging path and the settings screen read the same row.
            var prefs = await _prefRepo.GetByUserAsync(userId, cancellationToken);

            var emailEnabled = prefs?.EmailEnabled ?? true;
            var smsEnabled = prefs?.SmsEnabled ?? false;
            var voiceEnabled = prefs?.VoiceEnabled ?? false;
            var pushEnabled = prefs?.PushEnabled ?? true;

            if (prefs != null && IsInQuietHours(prefs) && payload.EventType != NotificationEventType.EscalationStep)
            {
                _logger.LogInformation(
                    "Skipping notification for {UserId} — quiet hours active",
                    userId);
                continue;
            }

            var channels = new List<NotificationType>(4);
            if (emailEnabled && _channelDispatchers.ContainsKey(NotificationType.Email))
                channels.Add(NotificationType.Email);
            if (smsEnabled && !string.IsNullOrEmpty(contact.PhoneNumber) && _channelDispatchers.ContainsKey(NotificationType.Sms))
                channels.Add(NotificationType.Sms);
            if (voiceEnabled && !string.IsNullOrEmpty(contact.PhoneNumber) && _channelDispatchers.ContainsKey(NotificationType.VoiceCall))
                channels.Add(NotificationType.VoiceCall);
            if (pushEnabled && _pushService != null)
                channels.Add(NotificationType.Push);

            // A channel this deployment could have used, left untried because the responder turned
            // it off. It produces no failure and no silence, so without this it leaves no trace.
            if (!emailEnabled && _channelDispatchers.ContainsKey(NotificationType.Email))
                optedOutChannels.Add(new DispatchChannelOptedOut(userId, NotificationType.Email));
            if (!smsEnabled && !string.IsNullOrEmpty(contact.PhoneNumber) && _channelDispatchers.ContainsKey(NotificationType.Sms))
                optedOutChannels.Add(new DispatchChannelOptedOut(userId, NotificationType.Sms));
            if (!voiceEnabled && !string.IsNullOrEmpty(contact.PhoneNumber) && _channelDispatchers.ContainsKey(NotificationType.VoiceCall))
                optedOutChannels.Add(new DispatchChannelOptedOut(userId, NotificationType.VoiceCall));

            // THIS RESPONDER IS HERE AND NOTHING WE HAVE CAN WAKE THEM: no phone number for the voice/SMS they enabled,
            // or only push left, or every paging channel off. A fact about a PROFILE, so it gets its own record.
            if (!channels.Any(Domain.Entities.Notification.ChannelCanPage))
            {
                unpageable++;
                var why = DescribeWhyUnpageable(emailEnabled, smsEnabled, voiceEnabled, contact.PhoneNumber);
                unpageableTargets.Add(new DispatchUnpageableTarget(userId, why));
                _logger.LogError(
                    "User {UserId} is on call for incident {IncidentId} but has NO channel that can page them ({Why}); "
                    + "this is not an empty rota and not a broken provider — it is their contact profile",
                    userId, payload.IncidentId, why);

                // The push toast (if any) still goes out below; it just cannot rescue the step.
                if (channels.Count == 0) continue;
            }

            // "paged" tracks only channels that can wake a human — see Notification.ChannelCanPage.
            // Push is dispatched below like any other channel (the UI wants it) but is never allowed
            // to set this flag: a browser toast to a closed laptop reports success, and while it
            // counted, a responder with no voice provider and no SMTP was recorded as reached.
            var paged = false;
            var lostChannels = new List<NotificationType>(channels.Count);
            var silentChannels = new List<DispatchChannelSilence>(channels.Count);
            var deferredChannels = new List<DispatchChannelDeferral>(channels.Count);

            // ONE ACCOUNTING FOR EVERY ROW WRITTEN, whatever happened to it. A voice cooldown and a cold provider registry
            // produce the IDENTICAL row, and calling either "silent" tells escalation nothing is in flight when a call is queued.
            void Account(Domain.Entities.Notification row)
            {
                if (!row.IsPagingChannel) return;

                if (row.CountsAsReached)
                {
                    paged = true;

                    // Reached, but not yet RUNG. Carried back so the incident can say so — a queued call
                    // and a ringing phone are not the same fact.
                    if (row.IsDeferredPage && row.NextRetryAt is { } nextAttempt)
                        deferredChannels.Add(new DispatchChannelDeferral(
                            userId, row.Type, row.ErrorMessage, nextAttempt));

                    return;
                }

                // Asked, and nothing came out — and nothing is coming. Carry the row's OWN reason back:
                // "no voice provider is registered" is a fault an operator can fix, and it is invisible
                // everywhere else.
                silentChannels.Add(new DispatchChannelSilence(
                    userId, row.Type, row.ErrorMessage, IsPermanent: !row.RetrySweepWillTakeIt));
                _logger.LogWarning(
                    "Notification on {Channel} for user {UserId} on incident {IncidentId} put no page on its way "
                    + "({Status}: {Error}); this channel does not count towards the step's reached count",
                    row.Type, userId, payload.IncidentId, row.DeliveryStatus, row.ErrorMessage);
            }

            // A DEDUPE HIT IS NOT A PAGE. The index says only that a row with this key exists; whether anybody was reached is a
            // fact about that row, so the row goes through the same Account(). PageIsOnItsWay keeps the double-page protection.
            void AccountForDedupeHit(
                Domain.Entities.Notification attempted, Domain.Entities.Notification? existing)
            {
                if (!attempted.IsPagingChannel) return;

                if (existing is null)
                {
                    // The index blocked the insert but the row cannot be read back (a hard delete between
                    // the two reads is the only way we know of). No evidence of a dead page, and a live
                    // one somewhere means a second send would ring the same phone twice — so keep the old
                    // reading and say so loudly.
                    paged = true;
                    _logger.LogError(
                        "A {Channel} notification for user {UserId} on incident {IncidentId} was blocked by the "
                        + "dedupe index, but the existing row could not be read back; counting the user as paged "
                        + "on the assumption that it is alive. If it was not, this step is over-reporting.",
                        attempted.Type, userId, payload.IncidentId);
                    return;
                }

                Account(existing);
            }

            foreach (var channel in channels)
            {
                var notification = NotificationFactory.Create(
                    userId, payload, incidentUrl, channel, payload.DispatchGeneration);

                // Voice-call cooldown (B0, opt-in): when another incident called this user
                // moments ago, persist the row DEFERRED instead of dialing — the normal
                // retry sweep places the call once the cooldown passes. Other channels
                // (email/SMS/push) are unaffected and still go out immediately.
                var deferral = await WhyNotNowAsync(channel, userId, payload, budget, cancellationToken);
                if (deferral is { } queued)
                {
                    notification.MarkDeferred(queued.Until, queued.Reason);
                    var deferClaim = await ClaimAsync(notification, cancellationToken);
                    if (deferClaim.Outcome == ClaimOutcome.Failed)
                    {
                        lostChannels.Add(channel);
                        continue;
                    }

                    if (deferClaim.Outcome == ClaimOutcome.RowAlreadyExists)
                    {
                        AccountForDedupeHit(notification, deferClaim.ExistingRow);
                        continue;
                    }

                    // No special case: a cooled-down call is a deferred page, and it goes through the
                    // same accounting as a page deferred for any other reason.
                    Account(notification);
                    _logger.LogInformation(
                        "[NOTIFICATION] {Channel} to {UserId} for incident {IncidentId} deferred until {Until:O}: {Reason}",
                        channel, userId, payload.IncidentId, queued.Until, queued.Reason);
                    continue;
                }

                notification.MarkSending();

                var claim = await ClaimAsync(notification, cancellationToken);
                if (claim.Outcome == ClaimOutcome.Failed)
                {
                    // A push row that will not persist is a lost toast, not a lost page. Folding it into
                    // ChannelFailures would report the step as DispatchFailed and re-run it — re-paging
                    // everyone who WAS reached — over a channel that could never have woken anybody.
                    if (notification.IsPagingChannel)
                        lostChannels.Add(channel);
                    else
                        _logger.LogWarning(
                            "Could not persist the {Channel} row for user {UserId} on incident {IncidentId}; "
                            + "this channel cannot page anyone, so the escalation step is unaffected",
                            channel, userId, payload.IncidentId);
                    continue;
                }

                // A dedupe hit means an equivalent row for THIS run already exists. Do not send a second
                // one — but do not assume the first one reached anybody either: ask the row.
                if (claim.Outcome == ClaimOutcome.RowAlreadyExists)
                {
                    AccountForDedupeHit(notification, claim.ExistingRow);
                    continue;
                }

                var startedAt = budget.StartOfAttempt();
                await SendClaimedAsync(notification, contact.Email, contact.PhoneNumber, payload, incidentUrl, cancellationToken);
                budget.ChargeSince(channel, startedAt);
                await PersistOutcomeAsync(notification, cancellationToken);

                // "Reached" is decided by what the send ACHIEVED, not by the row having been written — see Notification.CountsAsReached.
                // Push is sent and then dropped by Account(): it cannot wake anyone, and SignalR calls a send to nobody a success.
                Account(notification);
            }

            foreach (var lost in lostChannels)
                channelFailures.Add(new DispatchChannelFailure(userId, lost));
            channelSilences.AddRange(silentChannels);
            channelDeferrals.AddRange(deferredChannels);

            if (paged)
            {
                reached++;

                // The user WAS paged — but not on every channel they were meant to be. A voice call
                // whose claim failed is a phone that never rings, and nothing retries it, so it does
                // not get folded into "reached" and forgotten: it travels back in ChannelFailures.
                if (lostChannels.Count > 0)
                    _logger.LogError(
                        "Notification claim failed on {LostChannels} for user {UserId} on incident {IncidentId}; "
                        + "the user was paged on their other channel(s), but these pages were never queued and nothing will retry them",
                        string.Join(", ", lostChannels), userId, payload.IncidentId);

                if (silentChannels.Count > 0)
                    _logger.LogError(
                        "User {UserId} was paged on another channel for incident {IncidentId}, but {SilentCount} of their "
                        + "channel(s) sent nothing and never will ({Silences}); those channels are not configured",
                        userId, payload.IncidentId, silentChannels.Count,
                        string.Join("; ", silentChannels.Select(s => $"{s.Channel}: {s.Reason}")));

                if (deferredChannels.Count > 0)
                    _logger.LogInformation(
                        "User {UserId} has {DeferredCount} page(s) QUEUED but not yet sent for incident {IncidentId} ({Deferrals}); "
                        + "the retry sweep owns them and will send them at the deadline shown",
                        userId, deferredChannels.Count, payload.IncidentId,
                        string.Join("; ", deferredChannels.Select(d => $"{d.Channel}: {d.Reason} → {d.NextAttemptAt:O}")));
            }
            else if (lostChannels.Count > 0)
            {
                // Every paging channel for this user hit a store error, so nothing is queued and
                // nothing will retry. That is NOT "this user is unreachable" — the caller must be
                // able to tell the two apart, or a database blip gets written into the incident
                // timeline as an empty rota. See NotificationDispatchResult.
                failed++;
                _logger.LogError(
                    "Notification claim failed on all {ChannelCount} paging channel(s) for user {UserId} on incident {IncidentId}; this user was not paged",
                    lostChannels.Count, userId, payload.IncidentId);
            }
            else if (silentChannels.Count > 0)
            {
                // The user was on call with a channel that COULD have paged them, and every one of those channels sent nothing that
                // is ever coming. Not an empty rota: reported as one, the operator hunts the schedule and never sees the real fault.
                silent++;
                _logger.LogError(
                    "Every paging channel for user {UserId} on incident {IncidentId} sent nothing and never will ({Silences}); "
                    + "this user was NOT paged — the rota is fine, the channels are not configured",
                    userId, payload.IncidentId,
                    string.Join("; ", silentChannels.Select(s => $"{s.Channel}: {s.Reason}")));
            }
        }

        return new NotificationDispatchResult(
            reached, failed,
            channelFailures.Count > 0 ? channelFailures : null,
            silent,
            channelSilences.Count > 0 ? channelSilences : null,
            unpageable,
            unpageableTargets.Count > 0 ? unpageableTargets : null,
            channelDeferrals.Count > 0 ? channelDeferrals : null,
            optedOutChannels.Count > 0 ? optedOutChannels : null);
    }

    /// <summary>A page written down and postponed rather than sent now.</summary>
    private readonly record struct DispatchDeferral(DateTime Until, string Reason);

    /// <summary>Why this row is being queued instead of sent on this pass, or null to send it now.</summary>
    private async Task<DispatchDeferral?> WhyNotNowAsync(
        NotificationType channel,
        string userId,
        NotificationPayload payload,
        ChannelDispatchBudget budget,
        CancellationToken cancellationToken)
    {
        if (budget.IsSpent(channel))
            return new DispatchDeferral(budget.Now(), budget.Exhausted(channel));

        if (channel != NotificationType.VoiceCall || _voiceCoalescingGuard is null)
            return null;

        var deferUntil = await _voiceCoalescingGuard.GetDeferralUntilAsync(
            userId, payload.IncidentId, includeInFlight: true, cancellationToken);

        return deferUntil is null
            ? null
            : new DispatchDeferral(deferUntil.Value, "voice cooldown: recent call to same user");
    }

    /// <summary>Names what is missing for a responder no channel can page.</summary>
    // Only BLOCKERS are listed, and each names the thing the operator has to change; a channel that could page them never gets here.
    private string DescribeWhyUnpageable(bool emailEnabled, bool smsEnabled, bool voiceEnabled, string? phoneNumber)
    {
        var hasPhone = !string.IsNullOrWhiteSpace(phoneNumber);
        var blockers = new List<string>(3);

        Add(NotificationType.Email, emailEnabled, needsPhone: false, "e-mail");
        Add(NotificationType.Sms, smsEnabled, needsPhone: true, "SMS");
        Add(NotificationType.VoiceCall, voiceEnabled, needsPhone: true, "voice call");

        return string.Join("; ", blockers);

        void Add(NotificationType channel, bool enabled, bool needsPhone, string label)
        {
            if (!enabled)
                blockers.Add($"{label} is switched off in their notification preferences");
            else if (needsPhone && !hasPhone)
                blockers.Add($"{label} is enabled, but there is no phone number on their profile");
            else if (!_channelDispatchers.ContainsKey(channel))
                blockers.Add($"{label} is enabled, but no {label} dispatcher is registered on this host");
        }
    }

    /// <summary>Outcome of claiming a notification row.</summary>
    // RowAlreadyExists is deliberately not "already paged" — the index says a row EXISTS, nothing about who it reached, so the
    // existing row has to be looked at. Only Failed means nothing was queued at all.
    private enum ClaimOutcome { Claimed, RowAlreadyExists, Failed }

    /// <summary>The result of a claim, carrying the row that was already there on a dedupe hit.</summary>
    // ExistingRow is null in every other case, and also on a dedupe hit whose row cannot be read back.
    private readonly record struct ClaimResult(
        ClaimOutcome Outcome, Domain.Entities.Notification? ExistingRow = null);

    /// <summary>Commits a freshly-created notification row in its own unit of work, before any provider I/O.</summary>
    // Per-row so one bad row cannot sink a whole step's batch.
    private async Task<ClaimResult> ClaimAsync(Domain.Entities.Notification notification, CancellationToken cancellationToken)
    {
        await _notificationRepo.AddAsync(notification, cancellationToken);

        var isTracked = _dbContext.ChangeTracker.Entries<Domain.Entities.Notification>()
            .Any(e => ReferenceEquals(e.Entity, notification) && e.State == EntityState.Added);
        if (!isTracked)
        {
            // The repository declined the insert: a row with this DedupeKey is already there. What that
            // row ACHIEVED decides whether anybody was paged, so fetch it.
            return new ClaimResult(
                ClaimOutcome.RowAlreadyExists,
                await FindExistingByDedupeKeyAsync(notification, cancellationToken));
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ClaimResult(ClaimOutcome.Claimed);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Lost the race against a concurrent dispatch of the same step; that dispatch owns the page.
            _dbContext.Entry(notification).State = EntityState.Detached;
            _logger.LogDebug(
                "Notification claim lost the dedupe race: user={UserId} channel={Channel} incident={IncidentId}",
                notification.UserId, notification.Type, notification.IncidentId);
            return new ClaimResult(
                ClaimOutcome.RowAlreadyExists,
                await FindExistingByDedupeKeyAsync(notification, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _dbContext.Entry(notification).State = EntityState.Detached;
            _logger.LogError(ex,
                "Failed to persist notification claim: user={UserId} channel={Channel} incident={IncidentId}; skipping row",
                notification.UserId, notification.Type, notification.IncidentId);
            return new ClaimResult(ClaimOutcome.Failed);
        }
    }

    /// <summary>The row already sitting under this DedupeKey.</summary>
    // Read without the soft-delete filter because the partial unique index ignores it too, so a soft-deleted row still blocks the
    // insert. The ChangeTracker comes first: an in-flight duplicate is Added in this unit of work and not in the database yet.
    private async Task<Domain.Entities.Notification?> FindExistingByDedupeKeyAsync(
        Domain.Entities.Notification attempted, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(attempted.DedupeKey))
            return null;

        var pending = _dbContext.ChangeTracker.Entries<Domain.Entities.Notification>()
            .Where(e => !ReferenceEquals(e.Entity, attempted) && e.Entity.DedupeKey == attempted.DedupeKey)
            .Select(e => e.Entity)
            .FirstOrDefault();
        if (pending is not null)
            return pending;

        return await _dbContext.Notifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.DedupeKey == attempted.DedupeKey, cancellationToken);
    }

    /// <summary>Sends an already-claimed notification with no open transaction, recording the outcome on the entity.</summary>
    // Channel dispatchers already map provider exceptions to MarkFailed; the catch here only covers an unexpected throw.
    private async Task SendClaimedAsync(
        Domain.Entities.Notification notification,
        string? email,
        string? phone,
        NotificationPayload payload,
        string incidentUrl,
        CancellationToken cancellationToken)
    {
        if (notification.Type == NotificationType.Push)
        {
            await PushToUserAsync(notification, cancellationToken);
            return;
        }

        if (!_channelDispatchers.TryGetValue(notification.Type, out var dispatcher))
        {
            notification.MarkSkipped($"No dispatcher registered for channel {notification.Type}");
            return;
        }

        try
        {
            await dispatcher.SendAsync(notification, email, phone, payload, incidentUrl, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            notification.MarkFailed($"Channel dispatch error: {ex.Message}");
            _logger.LogError(ex,
                "Channel dispatch failed: channel={Channel} user={UserId} incident={IncidentId}",
                notification.Type, notification.UserId, payload.IncidentId);
        }
    }

    /// <summary>Commits the delivery outcome of a single tracked notification.</summary>
    // A failed commit leaves the row Sending and detaches it; the reaper reclaims it so nothing is silently lost.
    private async Task PersistOutcomeAsync(Domain.Entities.Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _dbContext.Entry(notification).State = EntityState.Detached;
            _logger.LogError(ex,
                "Failed to persist delivery outcome for notification {NotificationId}; the reaper will reclaim it",
                notification.Id);
        }
    }

    public async Task<NotificationDispatchResult> NotifyOnCallAsync(Guid scheduleId, NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        var onCallStatus = await _onCallService.GetCurrentOnCallAsync(scheduleId, bypassCache: true, cancellationToken);

        if (onCallStatus == null || string.IsNullOrEmpty(onCallStatus.PrimaryUserId))
        {
            NotificationDispatcherLog.NoOnCallUserFound(_logger, scheduleId);
            return NotificationDispatchResult.Nobody;
        }

        return await NotifyUsersAsync(BuildOnCallRecipients(onCallStatus, payload), payload, cancellationToken);
    }

    private static List<string> BuildOnCallRecipients(OnCallStatusDto status, NotificationPayload payload)
    {
        var userIds = new List<string> { status.PrimaryUserId! };
        if (payload.IncludeSecondaryOnCall && !string.IsNullOrEmpty(status.SecondaryUserId))
            userIds.Add(status.SecondaryUserId);
        return userIds;
    }

    public async Task<NotificationDispatchResult> NotifyTeamAsync(Guid teamId, NotificationPayload payload, bool notifyAllMembers, CancellationToken cancellationToken = default)
    {
        if (notifyAllMembers)
        {
            var memberIds = await _teamMemberRepo.GetQueryable()
                .Where(m => m.TeamId == teamId && !m.IsDeleted)
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (memberIds.Count == 0)
            {
                _logger.LogWarning("NotifyTeam: team {TeamId} has no members; step skipped", teamId);
                return NotificationDispatchResult.Nobody;
            }
            return await NotifyUsersAsync(memberIds, payload, cancellationToken);
        }

        var status = await _onCallService.GetCurrentOnCallForTeamAsync(teamId, bypassCache: true, cancellationToken);
        if (status is null || string.IsNullOrEmpty(status.PrimaryUserId))
        {
            _logger.LogWarning("NotifyTeam: team {TeamId} has no active on-call; step skipped", teamId);
            return NotificationDispatchResult.Nobody;
        }
        return await NotifyUsersAsync(BuildOnCallRecipients(status, payload), payload, cancellationToken);
    }

    public async Task ProcessNotificationQueueAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = await _organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);

        var claimed = await ClaimRetryBatchAsync(cancellationToken);
        if (claimed.Count == 0) return;

        NotificationDispatcherLog.ProcessingFailedNotifications(_logger, claimed.Count);

        var budget = new ChannelDispatchBudget(_clock, PerChannelDispatchBudget);

        foreach (var notification in claimed)
        {
            if (notification.DeliveryStatus == NotificationDeliveryStatus.Delivered)
                continue;

            // This channel has already held the sweep long enough. The row keeps its retry budget and its
            // place in the queue; what it does not do is keep every other channel's page waiting behind it.
            if (budget.IsSpent(notification.Type))
            {
                notification.MarkDeferred(budget.Now(), budget.Exhausted(notification.Type));
                await PersistOutcomeAsync(notification, cancellationToken);
                continue;
            }

            var startedAt = budget.StartOfAttempt();

            try
            {
                if (_channelDispatchers.TryGetValue(notification.Type, out var dispatcher))
                {
                    await dispatcher.RetryAsync(notification, baseUrl, cancellationToken);
                }
                else
                {
                    notification.MarkFailed($"Unsupported channel for retry: {notification.Type}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                notification.MarkFailed($"Retry: {ex.Message}");
                _logger.LogWarning(ex, "[RETRY] Failed notification {NotificationId} (attempt {Attempt})",
                    notification.Id, notification.RetryCount);
            }

            budget.ChargeSince(notification.Type, startedAt);
            await PersistOutcomeAsync(notification, cancellationToken);
        }
    }

    /// <summary>Claims a batch of retryable rows in a short transaction, so the send afterwards runs outside any transaction.</summary>
    // THIS QUERY IS THE DEFINITION OF Notification.RetrySweepWillTakeIt, which PageIsOnItsWay, IsDeferredPage and the escalation's
    // reached count all read as "a page is still coming" — so the retry bound is taken FROM MaxRetries, never typed in again.
    private async Task<List<Domain.Entities.Notification>> ClaimRetryBatchAsync(CancellationToken cancellationToken)
    {
        return await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var batch = await _dbContext.Notifications
                .FromSqlInterpolated($"""
                    SELECT *, xmin FROM "Notifications"
                    WHERE NOT "IsDeleted"
                      AND "DeliveryStatus" IN (0, 3)
                      AND "RetryCount" < {Domain.Entities.Notification.MaxRetries}
                      AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= NOW())
                    ORDER BY "CreatedAt"
                    LIMIT 50
                    FOR UPDATE SKIP LOCKED
                    """)
                .Include(n => n.Incident)
                .ToListAsync(cancellationToken);

            foreach (var notification in batch)
            {
                notification.MarkSending();
                notification.UpdatedAt = DateTime.UtcNow;
            }

            return batch;
        }, cancellationToken);
    }

    public async Task<(bool Success, string Message)> SendTestNotificationAsync(
        string userId, string channel, CancellationToken cancellationToken = default)
    {
        var contact = await _userContacts.GetContactByIdAsync(userId, cancellationToken);
        if (contact is null)
            return (false, "User not found");

        var channelType = channel.ToLowerInvariant() switch
        {
            "email" => NotificationType.Email,
            "sms" => NotificationType.Sms,
            "voice" => NotificationType.VoiceCall,
            _ => (NotificationType?)null
        };

        if (channelType == null)
            return (false, $"Unknown channel: {channel}");

        if (_channelDispatchers.TryGetValue(channelType.Value, out var dispatcher))
        {
            return await dispatcher.SendTestAsync(userId, contact.Email, contact.PhoneNumber, cancellationToken);
        }

        return (false, $"No dispatcher registered for channel: {channel}");
    }

    #region Helpers

    /// <summary>Pushes a real-time notification to the user's browser.</summary>
    // Sent because an open incident page updates itself, then dropped from the reached tally — see Notification.ChannelCanPage.
    private async Task PushToUserAsync(Domain.Entities.Notification notification, CancellationToken cancellationToken)
    {
        if (_pushService == null) return;

        try
        {
            var dto = new NotificationItemDto
            {
                Id = notification.Id,
                Title = notification.Title,
                Message = notification.Message,
                Type = MapType(notification.Type),
                ActionUrl = notification.ActionUrl,
                IsRead = false,
                CreatedAt = notification.CreatedAt,
                TimeAgo = "just now"
            };
            await _pushService.PushNotificationAsync(notification.UserId, dto, cancellationToken);
            notification.MarkDelivered();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            notification.MarkSkipped($"Real-time push unavailable: {ex.Message}");
            NotificationDispatcherLog.RealtimePushFailed(_logger, ex, notification.UserId);
        }
    }

    private static string MapType(Domain.Enums.NotificationType type) => type switch
    {
        Domain.Enums.NotificationType.VoiceCall => "escalation",
        _ => "info"
    };

    private bool IsInQuietHours(Domain.Entities.NotificationPreference prefs)
    {
        if (string.IsNullOrEmpty(prefs.QuietHoursStart) || string.IsNullOrEmpty(prefs.QuietHoursEnd))
            return false;

        if (!TimeOnly.TryParse(prefs.QuietHoursStart, out var start) ||
            !TimeOnly.TryParse(prefs.QuietHoursEnd, out var end))
            return false;

        var zone = _tzProvider.GetZoneOrNull(prefs.Timezone);
        if (zone is null)
        {
            _logger.LogWarning("QuietHours: unknown timezone '{Timezone}' for user {UserId} — not applying quiet hours",
                prefs.Timezone, prefs.UserId);
            return false;
        }

        var nowInZone = _clock.GetCurrentInstant().InZone(zone);
        var userNow = new TimeOnly(nowInZone.Hour, nowInZone.Minute, nowInZone.Second);

        if (start > end)
            return userNow >= start || userNow < end;
        return userNow >= start && userNow < end;
    }

    #endregion
}
