using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Callu.Shared.Logging;

namespace Callu.Infrastructure.Quartz;

/// <summary>Fires voice call retries whose <see cref="CallLog.NextRetryAt"/> deadline has elapsed; a due
/// row is a chain's last completed call, and only its deadline is ever this job's to move.</summary>
[DisallowConcurrentExecution]
public sealed class VoiceCallRetryQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<VoiceCallRetryQuartzJob> logger)
    : IJob
{
    private const int BatchSize = 25;

    /// <summary>Kept in step with VoximplantVoiceCallbackPersistence — both paths must stop at the same attempt.</summary>
    private const int MaxRetryAttempts = 3;
    private const int BackoffBaseSeconds = 30;
    private const int BackoffCapSeconds = 900;

    /// <summary>Shortest park for an ambiguous dial: long enough that a call which really was placed has
    /// produced its CallLog row, short enough that a dial that never went out is retried promptly.</summary>
    private const int AmbiguousDialFloorSeconds = 120;

    /// <summary>Wall-clock bound for a chain stuck without placing a call, measured from
    /// <see cref="CallLog.DialOutFailingSince"/> and never from <see cref="CallLog.CompletedAt"/>.</summary>
    private static readonly TimeSpan DialOutWindow = TimeSpan.FromHours(1);

    /// <summary>The bound for having had no voice provider to ask at all — deliberately far longer than
    /// <see cref="DialOutWindow"/>, because nobody has actually been asked yet.</summary>
    private static readonly TimeSpan ProviderUnavailableWindow = TimeSpan.FromHours(6);

    /// <summary>How long a claimed row stays out of the due window while its dial is in flight, and how
    /// long the chain waits before redoing the decision if the dial's outcome could not be persisted.</summary>
    private static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(5);

    private static TimeSpan WindowFor(VoiceDialOutFailureKind failure) => failure switch
    {
        VoiceDialOutFailureKind.ProviderMissing => ProviderUnavailableWindow,
        _ => DialOutWindow
    };

    /// <summary>The floor belongs to the failure that just happened, not to the kind governing the window.</summary>
    private static int FloorSecondsFor(VoiceDialOutFailureKind failure) => failure switch
    {
        VoiceDialOutFailureKind.ProviderThrew => AmbiguousDialFloorSeconds,
        _ => BackoffBaseSeconds
    };

    /// <summary>Which failure's window this stuck period is measured against: the most lenient kind it has
    /// seen, never the last to arrive, and the anchor is never reset.</summary>
    private static VoiceDialOutFailureKind GoverningKind(
        VoiceDialOutFailureKind? recorded, VoiceDialOutFailureKind current) =>
        recorded is { } previous && WindowFor(previous) >= WindowFor(current) ? previous : current;

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var providerRegistry = scope.ServiceProvider.GetRequiredService<ICommunicationProviderRegistry>();
        var callLogRepository = scope.ServiceProvider.GetRequiredService<ICallLogRepository>();

        var now = DateTime.UtcNow;
        var due = await db.CallLogs
            .Include(c => c.Incident)
            .Where(c => c.NextRetryAt != null &&
                        c.NextRetryAt <= now &&
                        !c.IsDeleted)
            .OrderBy(c => c.NextRetryAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        logger.LogInformation("VoiceCallRetry: {Count} due retries", due.Count);

        foreach (var callLog in due)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Fresh read: a batch of dials takes minutes, and the incident may have been picked
                // up in the meantime.
                var status = await LoadIncidentStatusAsync(db, callLog.IncidentId, ct);
                if (!IsStillPaging(status) || callLog.Incident is null)
                {
                    logger.LogInformation(
                        "VoiceCallRetry: incident {IncidentId} is {Status}; the pending retry to {Phone} stands down",
                        callLog.IncidentId, status?.ToString() ?? "gone", PiiRedactor.Phone(callLog.PhoneNumber));
                    await DisarmAsync(db, callLog, ct);
                    continue;
                }

                if (await AlreadyOnRecordUnderThisIdAsync(callLogRepository, callLog, ct))
                {
                    logger.LogInformation(
                        "VoiceCallRetry: a call is already on record under the id this retry would dial for incident "
                        + "{IncidentId} ({Phone}); that call owns the chain, so this retry stands down",
                        callLog.IncidentId, PiiRedactor.Phone(callLog.PhoneNumber));
                    await DisarmAsync(db, callLog, ct);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(callLog.PhoneNumber))
                {
                    logger.LogWarning(
                        "VoiceCallRetry: dropping retry for CallLog {CallLogId} — no phone number", callLog.Id);
                    await DisarmAsync(db, callLog, ct);
                    continue;
                }

                if (callLog.AttemptNumber >= MaxRetryAttempts)
                {
                    logger.LogWarning(
                        "VoiceCallRetry: max retry attempts ({Max}) reached for incident {IncidentId} ({Phone}); chain stopped",
                        MaxRetryAttempts, callLog.IncidentId, PiiRedactor.Phone(callLog.PhoneNumber));
                    await DisarmAsync(db, callLog, ct);
                    continue;
                }

                if (await NewerCallExistsAsync(db, callLog, ct))
                {
                    logger.LogInformation(
                        "VoiceCallRetry: a call to {Phone} for incident {IncidentId} already started after this row was armed; "
                        + "that call owns the chain, so this retry stands down",
                        PiiRedactor.Phone(callLog.PhoneNumber), callLog.IncidentId);
                    await DisarmAsync(db, callLog, ct);
                    continue;
                }

                // CLAIM, do not clear: take the row out of the due window without making it unselectable.
                // Everything from here to the outcome save is a separate commit that can be lost.
                callLog.NextRetryAt = DateTime.UtcNow.Add(ClaimWindow);
                if (!await TrySaveAsync(db, callLog, ct))
                    continue; // nothing committed — the row keeps its due NextRetryAt and the next tick retries it

                var provider = providerRegistry.GetProvider(CommunicationCapability.VoiceCalls);
                if (provider is null)
                {
                    // Not a failed call — a call we never got to attempt. On the first tick after the
                    // Worker starts this is simply the registry still warming up, and giving the chain
                    // up here would abandon a responder over a question no provider was ever asked.
                    logger.LogWarning(
                        "VoiceCallRetry: no voice provider available for incident {IncidentId}; the call was not attempted, re-arming",
                        callLog.IncidentId);
                    await ReArmAfterFailedDialOutAsync(db, callLog, VoiceDialOutFailureKind.ProviderMissing, ct);
                    continue;
                }

                var incident = callLog.Incident!;

                CallResult result;
                try
                {
                    result = await provider.MakeCallAsync(new MakeCallRequest
                    {
                        Destination = callLog.PhoneNumber,
                        IncidentId = incident.Id,
                        IncidentTitle = incident.Title,
                        Severity = incident.Severity.ToString(),
                        Description = incident.Description,
                        DataLanguage = incident.DataLanguage,
                        AttemptId = callLog.Id
                    });
                }
                catch (Exception ex) when (!IsShutdown(ex, ct))
                {
                    // A timeout says nothing about what the far end did, so do not dial again on that
                    // guess — park the row and let the next tick look for the CallLog row a call leaves.
                    logger.LogError(ex,
                        "VoiceCallRetry: provider threw for incident {IncidentId} ({Phone}) — cannot tell whether the call was placed. "
                        + "Not re-dialling; re-checking later",
                        callLog.IncidentId, PiiRedactor.Phone(callLog.PhoneNumber));
                    await ReArmAfterFailedDialOutAsync(db, callLog, VoiceDialOutFailureKind.ProviderThrew, ct);
                    continue;
                }

                if (result.Success)
                {
                    await SettleAfterCallPlacedAsync(db, callLog, ct);
                    continue;
                }

                // The provider answered, and its answer is "I did not place this call".
                logger.LogWarning(
                    "VoiceCallRetry failed for incident {IncidentId} ({Phone}): {Error}",
                    callLog.IncidentId, PiiRedactor.Phone(callLog.PhoneNumber), result.ErrorMessage);

                await ReArmAfterFailedDialOutAsync(db, callLog, VoiceDialOutFailureKind.ProviderRefused, ct);
            }
            catch (Exception ex) when (!IsShutdown(ex, ct))
            {
                logger.LogError(ex,
                    "VoiceCallRetry: unhandled error processing CallLog {CallLogId}", callLog.Id);
            }
        }
    }

    /// <summary>An Open incident is the only one still worth ringing a phone about.</summary>
    private static bool IsStillPaging(IncidentStatus? status) => status == IncidentStatus.Open;

    /// <summary>The incident's current status straight from the database, so the change tracker's copy
    /// cannot answer for it; null when the incident is gone or soft-deleted.</summary>
    private static Task<IncidentStatus?> LoadIncidentStatusAsync(
        ApplicationDbContext db, Guid incidentId, CancellationToken ct) =>
        db.Incidents
            .AsNoTracking()
            .Where(i => i.Id == incidentId)
            .Select(i => (IncidentStatus?)i.Status)
            .FirstOrDefaultAsync(ct);

    /// <summary>Whether a call to this responder for this incident started since the call this row records
    /// ended; the anchor must be <see cref="CallLog.CompletedAt"/>, which is never rewritten afterwards.</summary>
    private static Task<bool> NewerCallExistsAsync(ApplicationDbContext db, CallLog callLog, CancellationToken ct)
    {
        var since = callLog.CompletedAt ?? callLog.InitiatedAt;
        var normalized = Normalize(callLog.PhoneNumber);

        return db.CallLogs.AnyAsync(
            c => c.Id != callLog.Id &&
                 c.IncidentId == callLog.IncidentId &&
                 !c.IsDeleted &&
                 c.PhoneNumber.Replace("+", "").Replace(" ", "") == normalized &&
                 c.CreatedAt >= since,
            ct);
    }

    private static string Normalize(string phone) => phone.Replace("+", "").Replace(" ", "");

    /// <summary>Whether a call is already on record under the id this row would dial next — the same question <see cref="Services.VoiceCallChannelDispatcher"/> asks before its own first dial.</summary>
    private static Task<bool> AlreadyOnRecordUnderThisIdAsync(
        ICallLogRepository callLogs, CallLog callLog, CancellationToken ct) =>
        callLogs.AnyForAttemptAsync(callLog.Id, ct);

    /// <summary>Stands the chain down and commits; idempotent, so a lost save just stands it down again
    /// on the next tick.</summary>
    private Task<bool> DisarmAsync(ApplicationDbContext db, CallLog callLog, CancellationToken ct)
    {
        callLog.StandDownRetryChain();
        return TrySaveAsync(db, callLog, ct);
    }

    /// <summary>The call went out, so this row releases its claim and leaves no stuck-marker behind; the
    /// callback's new CallLog row owns the chain from here.</summary>
    private async Task SettleAfterCallPlacedAsync(ApplicationDbContext db, CallLog callLog, CancellationToken ct)
    {
        callLog.StandDownRetryChain();
        callLog.UpdatedAt = DateTime.UtcNow;

        if (!await TrySaveAsync(db, callLog, ct))
            logger.LogError(
                "VoiceCallRetry: the retry call to {Phone} for incident {IncidentId} WAS PLACED, but releasing the row failed. "
                + "It stays claimed until {ClaimWindow} from now, when the sweep will look for the call's own CallLog row and stand down.",
                PiiRedactor.Phone(callLog.PhoneNumber), callLog.IncidentId, ClaimWindow);
    }

    /// <summary>No call went out, so this row is still the chain and its deadline is pushed out, anchored
    /// on the first failed dial and re-reading the incident status rather than reusing it.</summary>
    private async Task ReArmAfterFailedDialOutAsync(
        ApplicationDbContext db, CallLog callLog, VoiceDialOutFailureKind failure, CancellationToken ct)
    {
        var status = await LoadIncidentStatusAsync(db, callLog.IncidentId, ct);
        if (!IsStillPaging(status))
        {
            logger.LogInformation(
                "VoiceCallRetry: incident {IncidentId} became {Status} while the dial was in flight; not re-arming the retry to {Phone}",
                callLog.IncidentId, status?.ToString() ?? "gone", PiiRedactor.Phone(callLog.PhoneNumber));
            await DisarmAsync(db, callLog, ct);
            return;
        }

        var now = DateTime.UtcNow;

        // First failure of this stuck period? Then the period starts NOW, and this tick is inside the
        // window by construction — no chain can be given up on before it has failed once.
        var failingSince = callLog.DialOutFailingSince ?? now;
        var stuckFor = now - failingSince;

        // Not "the window of the failure that just happened" — the window of the failure that GOVERNS
        // this stuck period. The marker is shared between failure kinds whose windows differ six-fold,
        // and reading it against the newest kind is how a chain gets charged for hours it never spent.
        var governing = GoverningKind(callLog.DialOutFailureKind, failure);
        var window = WindowFor(governing);

        if (stuckFor >= window)
        {
            await GiveUpAsync(db, callLog, failure, governing, window, now, ct);
            return;
        }

        callLog.DialOutFailingSince = failingSince;
        callLog.DialOutFailureKind = governing;
        callLog.NextRetryAt = now.AddSeconds(Math.Clamp(stuckFor.TotalSeconds, FloorSecondsFor(failure), BackoffCapSeconds));
        callLog.UpdatedAt = now;

        if (!await TrySaveAsync(db, callLog, ct))
            logger.LogWarning(
                "VoiceCallRetry: could not re-arm the retry to {Phone} for incident {IncidentId}; it keeps its claim and the next "
                + "sweep will retry the dial from durable state",
                PiiRedactor.Phone(callLog.PhoneNumber), callLog.IncidentId);
    }

    /// <summary>Stops a chain that has failed to place its call for the whole window, and records which of
    /// the three failures it was — "no provider to ask" is not "the provider refused".</summary>
    private async Task GiveUpAsync(
        ApplicationDbContext db,
        CallLog callLog,
        VoiceDialOutFailureKind failure,
        VoiceDialOutFailureKind governing,
        TimeSpan window,
        DateTime now,
        CancellationToken ct)
    {
        callLog.StandDownRetryChain();
        callLog.UpdatedAt = now;

        // The base fact is the failure that just happened — it is the most recent thing we know.
        var reason = failure switch
        {
            VoiceDialOutFailureKind.ProviderMissing =>
                "no voice provider was available to place it, so the call was never attempted",
            VoiceDialOutFailureKind.ProviderThrew =>
                "the voice provider stopped answering, so we cannot confirm whether any of those calls was placed",
            _ => "the voice provider would not place the call"
        };

        // A stuck period that ran under more than one kind of failure says so, rather than pinning the
        // whole window on whichever failure happened to be last. An operator reading "the provider would
        // not place the call" goes to look at the provider; if half of that window was in fact "there
        // was no provider configured to ask", that is the thing they need to be told.
        if (governing != failure && governing == VoiceDialOutFailureKind.ProviderMissing)
            reason += ", and for part of that time there was no voice provider available at all";

        logger.LogError(
            "VoiceCallRetry: could not place the retry call to {Phone} for incident {IncidentId} for {Window}; chain stopped ({Reason}). "
            + "Nobody may have been reached on this attempt — check the voice provider.",
            PiiRedactor.Phone(callLog.PhoneNumber), callLog.IncidentId, window, reason);

        // The operator's surface is the incident timeline, not the worker log. A retry chain that
        // gives up in silence is how "the page went out" turns into nobody answering the phone.
        db.Set<IncidentTimelineEvent>().Add(new IncidentTimelineEvent
        {
            IncidentId = callLog.IncidentId,
            EventType = TimelineEventType.CallFailed,
            Title = "Voice retry stopped",
            Description =
                $"Gave up retrying the call to {Recipient(callLog)} after {window.TotalMinutes:0} minutes — {reason}. "
                + "This responder may never have been reached.",
            ActorUserId = "system"
        });

        // Lost? The row keeps its claim and the next tick redoes the decision. A give-up that cannot be
        // written down must not be swallowed — it is the only signal this responder was never reached.
        if (!await TrySaveAsync(db, callLog, ct))
            logger.LogError(
                "VoiceCallRetry: the give-up for incident {IncidentId} ({Phone}) could not be recorded. The row keeps its claim and "
                + "the next sweep will reach this decision again — the operator has NOT been told yet.",
                callLog.IncidentId, PiiRedactor.Phone(callLog.PhoneNumber));
    }

    private static string Recipient(CallLog callLog) =>
        string.IsNullOrWhiteSpace(callLog.CalledPersonName)
            ? callLog.PhoneNumber
            : $"{callLog.CalledPersonName} ({callLog.PhoneNumber})";

    /// <summary>
    /// Persist one call log. On failure the row — and any timeline event staged alongside it — is
    /// detached so a poisoned entity cannot fail every later save in the batch as well.
    /// </summary>
    private async Task<bool> TrySaveAsync(ApplicationDbContext db, CallLog callLog, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (!IsShutdown(ex, ct))
        {
            db.Entry(callLog).State = EntityState.Detached;

            foreach (var staged in db.ChangeTracker.Entries<IncidentTimelineEvent>()
                         .Where(e => e.State == EntityState.Added)
                         .ToList())
                staged.State = EntityState.Detached;

            logger.LogError(ex,
                "VoiceCallRetry: could not persist CallLog {CallLogId}", callLog.Id);
            return false;
        }
    }

    private static bool IsShutdown(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;
}
