using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Callu.Shared.Extensions;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Notifications;
using Callu.Infrastructure.Telemetry;

namespace Callu.Infrastructure.Services;

/// <summary>Orchestrates incident escalation through policy steps.</summary>
// The page is dispatched FIRST and the pointer advances AFTER, in a separate transaction, so a crash re-runs the same
// step (the DedupeKey index makes that idempotent). This is the only code path that pages a step, for either caller.
public class EscalationOrchestrator(
    IIncidentRepository incidentRepo,
    IEscalationPolicyRepository escalationPolicyRepo,
    IIncidentTimelineEventRepository timelineRepo,
    ICallLogRepository callLogRepo,
    IAuditLogService auditLogService,
    ITransactionManager transactionManager,
    INotificationDispatcher notificationDispatcher,
    CalluMetrics metrics,
    ILogger<EscalationOrchestrator> logger,
    UserManager<ApplicationUser>? userManager = null) : IEscalationOrchestrator
{
    /// <summary>Longest target description written to the timeline.</summary>
    // The column is varchar(1000) and shares its budget with the rest of the sentence, so the
    // names are capped here rather than letting a wide team blow up the insert.
    private const int MaxTargetDescriptionLength = 300;

    // Shared with whatever reports when the next step is due, so the two cannot drift apart.
    private const int MinDelayMinutesBetweenSteps = Utilities.EscalationCalculations.MinDelayMinutesBetweenSteps;

    private const int ReconcileLookbackHours = 6;
    private const int ReconcileGraceMinutes = 5;

    /// <summary>How long a step whose page could not be queued is re-attempted before escalation moves on.</summary>
    // Measured from the FIRST failed attempt, not from when the step became due: an overdue step must not be burned without a retry.
    private static readonly TimeSpan DispatchRetryWindow = TimeSpan.FromMinutes(15);

    /// <summary>How long a placed call still counts as ringing before escalation stops waiting for it.</summary>
    // Derived from the step gap so the two cannot drift apart; an unbounded wait would bury the "nobody was reached" record forever.
    private static readonly TimeSpan InFlightPageWindow = TimeSpan.FromMinutes(MinDelayMinutesBetweenSteps);

    public async Task ProcessPendingEscalationsAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileUnactivatedEscalationsAsync(cancellationToken);

        var incidentIds = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .Where(i => i.IsEscalationActive &&
                        i.Status != IncidentStatus.Resolved &&
                        i.Status != IncidentStatus.Closed &&
                        i.Status != IncidentStatus.Acknowledged &&
                        i.EscalationPolicyId.HasValue &&
                        i.EscalationPolicy != null &&
                        !i.EscalationPolicy.IsDeleted &&
                        i.EscalationPolicy.IsActive)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        if (incidentIds.Count == 0) return;

        EscalationOrchestratorLog.EscalationRunStarted(logger, incidentIds.Count);

        foreach (var incidentId in incidentIds)
        {
            try
            {
                await ProcessOneIncidentAsync(incidentId, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex, "Escalation lost concurrency race for incident {IncidentId}; dropping tick", incidentId);
            }
            catch (Exception ex)
            {
                EscalationOrchestratorLog.EscalationProcessingError(logger, ex, incidentId);
            }
        }
    }

    /// <summary>Re-activates incidents whose escalation activation message never landed.</summary>
    // The normal sweep filters on IsEscalationActive and so can never recover these. Keyed on CreatedAt inside a bounded
    // window: the grace end keeps it from racing a healthy consumer, the lookback from re-escalating dormant rows.
    private async Task ReconcileUnactivatedEscalationsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddHours(-ReconcileLookbackHours);
        var windowEnd = now.AddMinutes(-ReconcileGraceMinutes);

        var candidates = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .Where(i => !i.IsEscalationActive &&
                        i.EscalationPolicyId == null &&
                        i.EscalationStartedAt == null &&
                        i.CurrentEscalationStepId == null &&
                        // Deliberately-unstaged incidents (paging suppressed by an alert rule,
                        // B3) are NOT dead-lettered triggers — never self-heal them.
                        !i.IsPagingSuppressed &&
                        i.TeamId != null &&
                        i.Status != IncidentStatus.Resolved &&
                        i.Status != IncidentStatus.Closed &&
                        i.Status != IncidentStatus.Acknowledged &&
                        i.CreatedAt >= windowStart &&
                        i.CreatedAt <= windowEnd)
            .Select(i => new { i.Id, i.TeamId, i.CreatedAt, i.EscalationStagedAt })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0) return;

        foreach (var candidate in candidates)
        {
            try
            {
                var policy = candidate.TeamId is null
                    ? null
                    : await escalationPolicyRepo.GetActiveForTeamAsync(candidate.TeamId.Value, cancellationToken);
                if (policy == null) continue;

                if (candidate.EscalationStagedAt is null && !PolicyPredatesIncident(policy, candidate.CreatedAt))
                    continue;

                logger.LogWarning(
                    "Reconciling incident {IncidentId}: escalation was never activated (likely a dead-lettered trigger); re-activating with policy {PolicyId}.",
                    candidate.Id, policy.Id);

                await TriggerEscalationAsync(candidate.Id, policy.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Reconcile failed to re-activate escalation for incident {IncidentId}", candidate.Id);
            }
        }
    }

    /// <summary>Stand-in for a missing <see cref="Incident.EscalationStagedAt"/> on rows that predate that column.</summary>
    // A policy created or edited after the incident means there was nothing to stage, so paging it retroactively would surprise everybody.
    private static bool PolicyPredatesIncident(EscalationPolicy policy, DateTime incidentCreatedAt) =>
        policy.CreatedAt <= incidentCreatedAt &&
        (policy.UpdatedAt ?? policy.CreatedAt) <= incidentCreatedAt;

    private Task ProcessOneIncidentAsync(Guid incidentId, CancellationToken cancellationToken) =>
        AdvanceStepAsync(incidentId, immediate: false, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> EscalateNowAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await AdvanceStepAsync(incidentId, immediate: true, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The sweep can drop a lost race; a keypress cannot — it happened once, on a phone, and for an exhausted or
            // acknowledged incident nothing follows. The race can be lost before OR after the dispatch, so neither the
            // log nor the timeline may claim to know whether a page went out.
            logger.LogError(ex,
                "Responder-initiated escalation for incident {IncidentId} lost a concurrency race: the escalation step was "
                + "not applied and the step pointer did not move. A page for that step may already have gone out. The sweep "
                + "re-runs the step only if the incident is still open with an active escalation.",
                incidentId);

            await TryRecordImmediateEscalationLostRaceAsync(incidentId, cancellationToken);
            return false;
        }
    }

    /// <summary>THE escalation step; the sweep and a responder pressing 2 both run this, differing only by <paramref name="immediate"/>.</summary>
    // Returns whether the step PAGED somebody, not whether a step ran — the phone speaks that answer to a responder still
    // holding it. `immediate` lifts the step's delay and never rewinds the pointer. Every false return leaves a reason on the timeline.
    private async Task<bool> AdvanceStepAsync(Guid incidentId, bool immediate, CancellationToken cancellationToken)
    {
        var plan = await BuildStepPlanAsync(incidentId, immediate, cancellationToken);
        if (plan == null) return false;

        metrics.EscalationStepTriggered(plan.Step.Level);
        EscalationOrchestratorLog.TriggeringEscalationStep(logger, plan.Step.Level, plan.IncidentId);

        var dispatch = await DispatchTargetAsync(plan, cancellationToken);

        await CommitStepAdvanceAsync(plan, dispatch, cancellationToken);
        return dispatch.Reached > 0;
    }

    /// <summary>Picks the step to page and freezes everything the dispatch needs into a plan.</summary>
    // Nothing is dispatched and the pointer is not moved here; the only writes committed are those that must survive a crash before dispatch.
    private Task<EscalationPlan?> BuildStepPlanAsync(Guid incidentId, bool immediate, CancellationToken cancellationToken) =>
        transactionManager.ExecuteInTransactionAsync<EscalationPlan?>(async () =>
        {
            var incident = await incidentRepo.GetQueryable()
                .Include(i => i.EscalationPolicy)
                    .ThenInclude(p => p!.Steps)
                        .ThenInclude(s => s.TargetedUsers)
                .Include(i => i.EscalationPolicy)
                    .ThenInclude(p => p!.Steps)
                        .ThenInclude(s => s.Schedule)
                .Include(i => i.EscalationPolicy)
                    .ThenInclude(p => p!.Steps)
                        .ThenInclude(s => s.Team)
                .Include(i => i.CurrentEscalationStep)
                .Include(i => i.Service)
                .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

            if (incident == null) return null;

            // Terminal stops everything. Acknowledged stops the SWEEP (a human has it) but not a
            // responder who is on the phone asking to hand it on — that is an explicit human
            // decision, and refusing it would strand the one person we know cannot take the page.
            if (incident.Status.IsTerminal())
            {
                if (immediate)
                    await RecordImmediateEscalationReachedNobodyAsync(incident,
                        "Escalation requested on the call, but the incident is already closed",
                        $"The responder asked to escalate during the call, but the incident was already {incident.Status}, "
                        + "so nobody was paged. If this is still happening, reopen the incident.",
                        $"Responder-initiated escalation: incident already {incident.Status}",
                        cancellationToken);
                return null;
            }

            if (!immediate && incident.Status == IncidentStatus.Acknowledged) return null;

            var steps = incident.EscalationPolicy?.Steps
                .Where(s => !s.IsDeleted)
                .OrderBy(s => s.Level)
                .ThenBy(s => s.CreatedAt)
                .ThenBy(s => s.Id)
                .ToList() ?? [];

            if (incident.EscalationPolicy == null || steps.Count == 0)
            {
                if (immediate)
                    await RecordImmediateEscalationReachedNobodyAsync(incident,
                        "Escalation requested on the call, but there is nowhere to escalate to",
                        "The responder asked to escalate during the call, but the incident has no escalation policy with usable steps, "
                        + "so nobody else was paged. Attach an escalation policy to the incident's team.",
                        "Responder-initiated escalation: no escalation policy with usable steps",
                        cancellationToken);
                return null;
            }

            var now = DateTime.UtcNow;
            EscalationStep? nextStep;
            bool exhausted = false;
            bool currentStepDeleted = false;
            var escalationWasActive = incident.IsEscalationActive;

            // The responder pressed 2 while escalation is idle. The pointer is NEVER rewound here: `immediate` also lifts the
            // delay gate, so rewinding would ring the person who just said they cannot take it. A run that is over stays over.
            if (immediate && !incident.IsEscalationActive)
            {
                if (incident.EscalationStartedAt is null && incident.CurrentEscalationStepId is null)
                {
                    // Escalation has genuinely never run for this incident (no trigger ever landed,
                    // paging suppressed at creation, a policy attached afterwards). Not one page has
                    // gone out under this policy, so starting the run at step 1 cannot ring the caller
                    // back for pressing 2 — it is precisely the escalation they asked for.
                    incident.EscalationStartedAt = now;
                    incident.LastEscalationStepAt = null;
                    incident.DispatchFailingSince = null;
                    incident.IsEscalationActive = SweepMayContinue(incident);
                }
                else if (incident.CurrentEscalationStepId is null)
                {
                    // A run DID happen, but the pointer that says how far it got is gone (releases up
                    // clear it when a phone acknowledgement lands), so "the next step" cannot
                    // be identified. Guessing step 1 would re-page everyone the previous run already
                    // paged — starting with the responder still holding the phone. Refuse, out loud.
                    await RecordImmediateEscalationReachedNobodyAsync(incident,
                        "Escalation requested on the call, but the escalation could not be resumed",
                        "The responder asked to escalate during the call, but this incident's escalation no longer records which "
                        + "step it had reached, so the next step cannot be identified. It was deliberately NOT restarted from step 1 — "
                        + "that would have re-paged everyone the earlier run already paged, including the responder who just asked to be "
                        + "relieved. Nobody was paged: escalate from the incident page, or page someone by hand.",
                        "Responder-initiated escalation: step pointer missing, cannot resume without re-paging the caller",
                        cancellationToken);
                    return null;
                }

                // Otherwise the pointer is intact — fall through and resume from it, exactly as an
                // active escalation would.
            }

            if (incident.CurrentEscalationStepId == null)
            {
                nextStep = steps[0];

                if (IsRepeatDurationExceeded(incident, incident.EscalationPolicy, now))
                {
                    incident.IsEscalationActive = false;
                    exhausted = true;
                    nextStep = null;
                }
                else if (immediate && incident.EscalationCyclesCompleted > 0)
                {
                    // Between passes the pointer is null because the run restarts, not because it was lost.
                    await RecordImmediateEscalationReachedNobodyAsync(incident,
                        "Escalation requested on the call, but the policy is between passes",
                        "The responder asked to escalate during the call, but this incident's escalation policy had just "
                        + "finished a full pass and the next one has not started yet, so there is no later step to hand on "
                        + "to. It was deliberately NOT restarted from step 1 — that would have re-paged everyone the "
                        + "previous pass already paged, including the responder who just asked to be relieved. Nobody was "
                        + "paged: the next pass pages on its own schedule, or escalate from the incident page.",
                        "Responder-initiated escalation: policy between repeat passes, cannot restart without re-paging the caller",
                        cancellationToken);
                    return null;
                }
                else if (incident.EscalationCyclesCompleted > 0 && incident.LastEscalationStepAt is DateTime cycleAnchor)
                {
                    var effectiveDelay = Math.Max(nextStep.DelayMinutes, MinDelayMinutesBetweenSteps);
                    if (!immediate && !ShouldTriggerStep(cycleAnchor, effectiveDelay, now))
                        return null;
                }
                else
                {
                    var startedAt = incident.EscalationStartedAt;
                    if (startedAt is null)
                    {
                        EscalationOrchestratorLog.EscalationTimestampMissing(logger, incident.Id,
                            nameof(Incident.EscalationStartedAt), nameof(Incident.CreatedAt));
                        startedAt = incident.CreatedAt;
                        incident.EscalationStartedAt = startedAt;
                    }

                    if (!immediate && !ShouldTriggerStep(startedAt.Value, nextStep.DelayMinutes, now))
                        return null;
                }
            }
            else
            {
                var currentStepIndex = steps.FindIndex(s => s.Id == incident.CurrentEscalationStepId);

                int resumeIndex;
                if (currentStepIndex >= 0)
                {
                    resumeIndex = currentStepIndex + 1;
                }
                else
                {
                    currentStepDeleted = true;

                    var deletedLevel = await incidentRepo.GetQueryable()
                        .IgnoreQueryFilters()
                        .Where(i => i.Id == incident.Id && i.CurrentEscalationStepId != null)
                        .Select(i => (int?)i.CurrentEscalationStep!.Level)
                        .FirstOrDefaultAsync(cancellationToken);

                    resumeIndex = deletedLevel is int deleted
                        ? steps.FindIndex(s => s.Level > deleted)
                        : 0;
                }

                if (resumeIndex < 0 || resumeIndex >= steps.Count)
                {
                    // "No step left" is not yet "nobody answered": the last step's call can still be ringing, and
                    // finalising here records a run that ended unacknowledged while the responder is holding the phone.
                    // Not for `immediate`: there the caller IS the in-flight call and is owed an answer now.
                    if (!immediate && await HasPageStillInFlightAsync(incident.Id, now, cancellationToken))
                    {
                        EscalationOrchestratorLog.EscalationWaitingOnInFlightPage(logger, incident.Id);
                        return null;
                    }

                    if (!immediate && TryBeginNextCycle(incident, incident.EscalationPolicy, now))
                    {
                        await timelineRepo.AddAsync(new IncidentTimelineEvent
                        {
                            IncidentId = incident.Id,
                            EventType = TimelineEventType.Escalated,
                            Title = "Escalation repeating",
                            Description =
                                $"Policy finished a full pass without acknowledgement; restarting from step 1 "
                                + $"(cycle {incident.EscalationCyclesCompleted + 1} of {ClampedMaxCycles(incident.EscalationPolicy)}).",
                            ActorUserId = "system"
                        }, cancellationToken);

                        EscalationOrchestratorLog.EscalationCompleted(logger, incident.Id);
                        return null;
                    }

                    incident.EscalationCyclesCompleted = Math.Min(
                        incident.EscalationCyclesCompleted + 1,
                        ClampedMaxCycles(incident.EscalationPolicy));
                    incident.IsEscalationActive = false;
                    exhausted = true;
                    nextStep = null;
                    EscalationOrchestratorLog.EscalationCompleted(logger, incident.Id);
                }
                else if (IsRepeatDurationExceeded(incident, incident.EscalationPolicy, now))
                {
                    incident.IsEscalationActive = false;
                    exhausted = true;
                    nextStep = null;
                    EscalationOrchestratorLog.EscalationCompleted(logger, incident.Id);
                }
                else
                {
                    nextStep = steps[resumeIndex];

                    if (currentStepDeleted)
                        logger.LogWarning(
                            "Escalation for incident {IncidentId} resumed at step level {Level}: the previously active step was deleted from policy {PolicyId}.",
                            incident.Id, nextStep.Level, incident.EscalationPolicyId);

                    if (!immediate)
                    {
                        var lastStepAt = incident.LastEscalationStepAt;
                        if (lastStepAt is null)
                        {
                            EscalationOrchestratorLog.EscalationTimestampMissing(logger, incident.Id,
                                nameof(Incident.LastEscalationStepAt), nameof(Incident.EscalationStartedAt));
                            lastStepAt = incident.EscalationStartedAt ?? incident.CreatedAt;
                            incident.LastEscalationStepAt = lastStepAt;
                        }

                        var effectiveDelay = Math.Max(nextStep.DelayMinutes, MinDelayMinutesBetweenSteps);
                        if (!ShouldTriggerStep(lastStepAt.Value, effectiveDelay, now))
                            return null;
                    }
                }
            }

            if (exhausted)
            {
                // Only the run that was still going gets an "exhausted" record. A responder pressing 2
                // on an escalation that ended some time ago must not stamp the timeline with a second
                // (and a third) copy of a fact that was already recorded when it actually happened.
                if (escalationWasActive)
                {
                    var exhaustionDetail = DescribeExhaustion(incident, incident.EscalationPolicy, currentStepDeleted, now);

                    await timelineRepo.AddAsync(new IncidentTimelineEvent
                    {
                        IncidentId = incident.Id,
                        EventType = TimelineEventType.Escalated,
                        Title = "Escalation exhausted",
                        Description = exhaustionDetail.Timeline,
                        ActorUserId = "system"
                    }, cancellationToken);

                    await auditLogService.LogAsync(
                        EscalationActor, AuditAction.EscalationExhausted, "Incident", incident.Id.ToString(),
                        null, exhaustionDetail.Audit,
                        cancellationToken: cancellationToken);

                    logger.LogError(
                        "Escalation policy {PolicyId} for incident {IncidentId} stopped without acknowledgement — {Reason}",
                        incident.EscalationPolicyId, incident.Id, exhaustionDetail.Audit);
                }

                // The responder asked to be relieved and the policy has nobody left to ask. Restarting
                // it would page the people it has already paged — the caller first — so the honest
                // answer is the only one: nobody was reached, a human has to take it from here.
                if (immediate)
                    await RecordImmediateEscalationReachedNobodyAsync(incident,
                        "Escalation requested on the call, but the policy is exhausted",
                        "The responder asked to escalate during the call, but every step of the escalation policy has already been "
                        + "paged and there is no later step to hand the incident to. Nobody else was paged — the escalation was NOT "
                        + "restarted from step 1, which would only have re-paged the same people, the caller included. "
                        + "Page someone by hand for this incident.",
                        "Responder-initiated escalation: policy already exhausted, no later step to page",
                        cancellationToken);

                return null;
            }

            if (nextStep == null) return null;

            var target = ResolveTarget(nextStep, await ResolveUserNamesAsync(nextStep, cancellationToken));

            return new EscalationPlan(
                IncidentId: incident.Id,
                Step: nextStep,
                Payload: new NotificationPayload
                {
                    IncidentId = incident.Id,
                    Title = incident.Title,
                    Description = incident.Description,
                    Severity = incident.Severity.ToString(),
                    EventType = NotificationEventType.EscalationStep,
                    EscalationLevel = nextStep.Level,
                    ServiceName = incident.Service?.Name,
                    DataLanguage = incident.DataLanguage,
                    IncludeSecondaryOnCall = nextStep.NotifyBothOnCall,
                    DispatchGeneration = ComputeDispatchGeneration(
                        incident.EscalationStartedAt, incident.EscalationCyclesCompleted)
                },
                Target: target,
                FailingSince: incident.DispatchFailingSince,
                Immediate: immediate,
                PolicyIsSweepable: incident.EscalationPolicy.IsActive);
        }, cancellationToken);

    /// <summary>Records on the timeline and in the audit log that a responder-initiated escalation paged nobody.</summary>
    // Called inside the plan transaction. Standing invariant: EscalateNowAsync returning false must always leave one of these records.
    private async Task RecordImmediateEscalationReachedNobodyAsync(
        Incident incident, string title, string description, string auditDetail, CancellationToken cancellationToken)
    {
        logger.LogError(
            "Responder-initiated escalation for incident {IncidentId} paged nobody: {Reason} (policy {PolicyId}).",
            incident.Id, auditDetail, incident.EscalationPolicyId);

        await timelineRepo.AddAsync(new IncidentTimelineEvent
        {
            IncidentId = incident.Id,
            EventType = TimelineEventType.Escalated,
            Title = title,
            Description = description,
            ActorUserId = "system"
        }, cancellationToken);

        await auditLogService.LogAsync(
            EscalationActor, AuditAction.EscalationNobodyReached, "Incident", incident.Id.ToString(),
            null, auditDetail, cancellationToken: cancellationToken);
    }

    /// <summary>Best-effort record that a responder-initiated step lost a concurrency race and was never applied.</summary>
    // Deliberately does NOT claim nobody was paged: the race can be lost after the dispatch, losing only the record of it.
    private async Task TryRecordImmediateEscalationLostRaceAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        try
        {
            await transactionManager.ExecuteInTransactionAsync(async () =>
            {
                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incidentId,
                    EventType = TimelineEventType.Escalated,
                    Title = "Escalation requested on the call was not applied",
                    Description =
                        "The responder asked to escalate during the call, but the incident was being changed at the same moment "
                        + "(an acknowledgement landing right then is the usual cause), so the escalation step was not applied and "
                        + "escalation did not move on. A page for that step may already have gone out; if it did, it is not "
                        + "recorded here. The escalation sweep re-runs this step only while the incident is still open with an "
                        + "active escalation — otherwise nobody else is coming and someone has to be paged by hand.",
                    ActorUserId = "system"
                }, cancellationToken);

                await auditLogService.LogAsync(
                    EscalationActor, AuditAction.EscalationNobodyReached, "Incident", incidentId.ToString(),
                    null, "Responder-initiated escalation: lost a concurrency race, the step was not applied",
                    cancellationToken: cancellationToken);

                return true;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not record the dropped responder-initiated escalation for incident {IncidentId} on the timeline.",
                incidentId);
        }
    }

    /// <summary>Who the escalation engine is recorded as.</summary>
    private const string EscalationActor = "system:escalation";

    /// <summary>Advances the step pointer and records the "step triggered" timeline, AFTER the page has been dispatched.</summary>
    // A concurrent ack/resolve in the dispatch window cannot be committed over: either CanStillCommit refuses, or the stale
    // xmin throws. A dispatch that FAILED commits no advance at all, only the first-failure marker.
    private async Task CommitStepAdvanceAsync(EscalationPlan plan, NotificationDispatchResult dispatch, CancellationToken cancellationToken)
    {
        foreach (var failure in dispatch.ChannelFailures ?? [])
            metrics.NotificationFailed(failure.Channel.ToString(), "claim-failed");

        var now = DateTime.UtcNow;
        var failingSince = plan.FailingSince ?? now;
        var stuckFor = now - failingSince;

        if (dispatch.DispatchFailed && stuckFor < DispatchRetryWindow)
        {
            await RecordDispatchFailingAsync(plan, dispatch, failingSince, cancellationToken);
            return;
        }

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = await incidentRepo.GetByIdAsync(plan.IncidentId, cancellationToken);
            if (incident == null) return false;

            if (!CanStillCommit(incident, plan)) return false;

            ApplyImmediateEscalationFlag(incident, plan);

            incident.CurrentEscalationStepId = plan.Step.Id;

            // This step is done being re-attempted — whichever way it ended. The next step starts
            // its own retry window from its own first failure.
            incident.DispatchFailingSince = null;

            var committedAt = DateTime.UtcNow;

            // BACKDATING THE STEP CLOCK IS A LOADED GUN: the next tick fires the next step at once. Correct only when NOT ONE
            // PAGE IS IN FLIGHT — no ack can arrive from an empty rota, an unpageable responder or a permanently silent channel.
            // AllSilencesPermanent is what keeps a silence that still has a retry coming from ever backdating this clock.
            var nothingIsComing =
                dispatch.NobodyToPage
                || dispatch.TargetsUnpageable
                || (dispatch.ChannelsSilent && dispatch.AllSilencesPermanent);

            var nothingToWaitFor = nothingIsComing &&
                                   plan.Target.Kind is EscalationTargetKind.Schedule or EscalationTargetKind.Team;

            incident.LastEscalationStepAt = nothingToWaitFor
                ? committedAt.AddMinutes(-(MinDelayMinutesBetweenSteps + 1))
                : committedAt;

            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = incident.Id,
                EventType = TimelineEventType.Escalated,
                EscalationStepId = plan.Step.Id,
                Title = $"Escalation step {plan.Step.Level} triggered",
                Description = plan.Target.Description,
                ActorUserId = "system"
            }, cancellationToken);

            // One row per step, not per target: the auditor asks which steps ran, and a step that
            // paged eight people is one act of the policy.
            await auditLogService.LogAsync(
                EscalationActor, AuditAction.Escalated, "Incident", incident.Id.ToString(),
                null, $"Step {plan.Step.Level} ({plan.Target.Kind}): {dispatch.Reached} of {dispatch.Reached + dispatch.Failed} target(s) reached",
                description: $"Escalation step {plan.Step.Level} — {plan.Target.Description}",
                cancellationToken: cancellationToken);

            if (dispatch.DispatchFailed)
            {
                logger.LogError(
                    "Escalation step {Level} for incident {IncidentId} could not be dispatched for {StuckMinutes:F0} minutes ({Channels}); "
                    + "giving up on this step so the policy's later steps still run.",
                    plan.Step.Level, plan.IncidentId, stuckFor.TotalMinutes, dispatch.DescribeChannelFailures());

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incident.Id,
                    EventType = TimelineEventType.Escalated,
                    Title = $"Escalation step {plan.Step.Level}: page could not be queued (gave up)",
                    Description =
                        $"The notification store rejected every page for this step ({dispatch.Failed} target(s) affected) for "
                        + $"{stuckFor.TotalMinutes:F0} minutes, so nobody was contacted. "
                        + "Escalation moved on to keep the later steps running — check the notification pipeline.",
                    ActorUserId = "system"
                }, cancellationToken);

                await auditLogService.LogAsync(
                    EscalationActor, AuditAction.EscalationDispatchFailed, "Incident", incident.Id.ToString(),
                    null, $"Step {plan.Step.Level} ({plan.Target.Kind}): notification claim failed for {dispatch.Failed} target(s) [{dispatch.DescribeChannelFailures()}]",
                    cancellationToken: cancellationToken);
            }
            else if (dispatch.PartiallyFailed)
            {
                // Someone was paged, so the step legitimately advances — but a channel that was
                // meant to page somebody never made it into the store, and nothing will retry it.
                // Folding this into "reached" is how a responder's phone stays silent while the
                // timeline claims the step went out.
                logger.LogError(
                    "Escalation step {Level} for incident {IncidentId} reached {Reached} target(s), but {Lost} page(s) could not be queued ({Channels}); nothing will retry them.",
                    plan.Step.Level, plan.IncidentId, dispatch.Reached, dispatch.FailedChannelCount, dispatch.DescribeChannelFailures());

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incident.Id,
                    EventType = TimelineEventType.Escalated,
                    Title = $"Escalation step {plan.Step.Level}: some pages could not be queued",
                    Description =
                        $"{dispatch.Reached} target(s) were paged, but {dispatch.FailedChannelCount} page(s) were rejected by the notification store "
                        + $"and nothing will retry them ({dispatch.DescribeChannelFailures()}). Those responders were not contacted on those channels — "
                        + "check the notification pipeline.",
                    ActorUserId = "system"
                }, cancellationToken);

                await auditLogService.LogAsync(
                    EscalationActor, AuditAction.EscalationDispatchPartiallyFailed, "Incident", incident.Id.ToString(),
                    null, $"Step {plan.Step.Level} ({plan.Target.Kind}): reached {dispatch.Reached}, lost {dispatch.FailedChannelCount} page(s) [{dispatch.DescribeChannelFailures()}]",
                    cancellationToken: cancellationToken);
            }
            else if (dispatch.PartiallySilent)
            {
                // Somebody was paged, so the step rightly advances — but a channel that was meant to
                // page somebody sent nothing and never will. The classic shape is a secondary on-call
                // whose voice provider is missing while the primary's e-mail went out: the step reads
                // as a success and one responder's phone simply never rings, for good.
                logger.LogError(
                    "Escalation step {Level} for incident {IncidentId} reached {Reached} target(s), but {Silent} channel(s) sent nothing "
                    + "and never will ({Silences}); those channels are not configured.",
                    plan.Step.Level, plan.IncidentId, dispatch.Reached, dispatch.SilentChannelCount, dispatch.DescribeChannelSilences());

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incident.Id,
                    EventType = TimelineEventType.Escalated,
                    Title = $"Escalation step {plan.Step.Level}: some channels could not send",
                    Description =
                        $"{dispatch.Reached} target(s) were paged, but {dispatch.SilentChannelCount} channel(s) sent nothing and "
                        + $"nothing will retry them: {dispatch.DescribeChannelSilences()}. Those responders were not contacted on "
                        + "those channels — configure the channel(s) above.",
                    ActorUserId = "system"
                }, cancellationToken);

                await auditLogService.LogAsync(
                    EscalationActor, AuditAction.EscalationChannelsPartiallySilent, "Incident", incident.Id.ToString(),
                    null, $"Step {plan.Step.Level} ({plan.Target.Kind}): reached {dispatch.Reached}, {dispatch.SilentChannelCount} silent channel(s) [{dispatch.DescribeChannelSilences()}]",
                    cancellationToken: cancellationToken);
            }

            // Somebody was paged and somebody ELSE on this step has no channel that could page them.
            // Same shape as a silent channel, different remedy — and it is written whether or not the
            // silent branch above ran, because "your secondary on-call has no phone number" is not
            // something any other branch says.
            if (dispatch.PartiallyUnpageable)
            {
                logger.LogError(
                    "Escalation step {Level} for incident {IncidentId} reached {Reached} target(s), but {Unpageable} on-call "
                    + "responder(s) have NO channel that can page them ({Targets}); their contact profiles are the fault.",
                    plan.Step.Level, plan.IncidentId, dispatch.Reached, dispatch.Unpageable, dispatch.DescribeUnpageableTargets());

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incident.Id,
                    EventType = TimelineEventType.Escalated,
                    Title = $"Escalation step {plan.Step.Level}: a responder has no channel that can page them",
                    Description =
                        $"{dispatch.Reached} target(s) were paged, but {dispatch.Unpageable} on-call responder(s) could not be "
                        + $"contacted by anything: {dispatch.DescribeUnpageableTargets()}. This is a contact-profile gap, not an "
                        + "empty rota — add a phone number or enable a paging channel on their profile.",
                    ActorUserId = "system"
                }, cancellationToken);

                await auditLogService.LogAsync(
                    EscalationActor, AuditAction.EscalationTargetsUnpageable, "Incident", incident.Id.ToString(),
                    null, $"Step {plan.Step.Level} ({plan.Target.Kind}): reached {dispatch.Reached}, {dispatch.Unpageable} unpageable target(s) [{dispatch.DescribeUnpageableTargets()}]",
                    cancellationToken: cancellationToken);
            }

            // A PAGE ON ITS WAY IS NOT A PHONE THAT RANG. Deferred pages count as reached (the retry sweep owns them), but a green
            // "step triggered" must not be the only sign that every call this step placed is still queued.
            if (dispatch.HasDeferredPages)
            {
                logger.LogWarning(
                    "Escalation step {Level} for incident {IncidentId} has {Deferred} page(s) QUEUED BUT NOT YET SENT ({Deferrals}); "
                    + "the retry sweep will send them at the deadlines shown — no phone has rung for them yet.",
                    plan.Step.Level, plan.IncidentId, dispatch.DeferredChannelCount, dispatch.DescribeDeferrals());

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incident.Id,
                    EventType = TimelineEventType.Escalated,
                    Title = $"Escalation step {plan.Step.Level}: {dispatch.DeferredChannelCount} page(s) queued, not yet sent",
                    Description =
                        $"The page(s) for this step are queued and will be sent automatically, but they have NOT gone out yet and "
                        + $"no phone has rung for them: {dispatch.DescribeDeferrals()}. A voice cooldown clears on its own. A "
                        + "missing provider does not — if no provider appears, these pages are abandoned after 30 minutes, so "
                        + "check Settings → Communications if this persists.",
                    ActorUserId = "system"
                }, cancellationToken);
            }

            return true;
        }, cancellationToken);
    }

    /// <summary>Leaves the pointer put after a page that could not be queued, and stamps the FIRST such failure on the incident.</summary>
    // Only the first failing tick writes, so the timeline gets one entry rather than one per tick; and "retrying" is claimed
    // only when the sweep will in fact pick the incident up again.
    private async Task RecordDispatchFailingAsync(
        EscalationPlan plan, NotificationDispatchResult dispatch, DateTime failingSince, CancellationToken cancellationToken)
    {
        if (plan.FailingSince is not null)
        {
            logger.LogError(
                "Escalation step {Level} for incident {IncidentId} still cannot be queued ({Channels}); "
                + "the step pointer is unchanged and escalation gives up on it after {Window:g} of failing.",
                plan.Step.Level, plan.IncidentId, dispatch.DescribeChannelFailures(), DispatchRetryWindow);
            return;
        }

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = await incidentRepo.GetByIdAsync(plan.IncidentId, cancellationToken);
            if (incident == null) return false;

            if (!CanStillCommit(incident, plan) || incident.DispatchFailingSince is not null)
                return false;

            ApplyImmediateEscalationFlag(incident, plan);

            var sweepWillRetry = SweepWillRetry(incident, plan);

            logger.LogError(
                "Escalation step {Level} for incident {IncidentId} paged nobody because the notification claim failed for all {Failed} target(s) ({Channels}). "
                + "The step pointer is unchanged. Sweep re-runs this step: {SweepWillRetry} (it gives up after {Window:g} of failing).",
                plan.Step.Level, plan.IncidentId, dispatch.Failed, dispatch.DescribeChannelFailures(), sweepWillRetry, DispatchRetryWindow);

            incident.DispatchFailingSince = failingSince;

            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = incident.Id,
                EventType = TimelineEventType.Escalated,
                Title = sweepWillRetry
                    ? $"Escalation step {plan.Step.Level}: page could not be queued (retrying)"
                    : $"Escalation step {plan.Step.Level}: page could not be queued (nobody was contacted)",
                Description = sweepWillRetry
                    ? $"The notification store rejected every page for this step ({dispatch.Failed} target(s) affected), so nobody has been contacted yet. "
                      + "Escalation keeps re-trying this same step — check the notification pipeline."
                    : $"The notification store rejected every page for this step ({dispatch.Failed} target(s) affected), so nobody was contacted. "
                      + "The escalation sweep does not re-run this incident (it is acknowledged), so no automatic retry is coming — "
                      + "page someone by hand and check the notification pipeline.",
                ActorUserId = "system"
            }, cancellationToken);

            await auditLogService.LogAsync(
                EscalationActor, AuditAction.EscalationDispatchFailing, "Incident", incident.Id.ToString(),
                null, $"Step {plan.Step.Level} ({plan.Target.Kind}): notification claim failed for {dispatch.Failed} target(s) [{dispatch.DescribeChannelFailures()}]; "
                    + (sweepWillRetry ? "re-trying" : "no automatic retry (incident is not swept)"),
                cancellationToken: cancellationToken);

            return true;
        }, cancellationToken);
    }

    /// <summary>Whether this dispatch's outcome may still be written to the incident.</summary>
    // A responder-initiated step is judged only on terminal status, because its page is already on its way; keeping it from
    // running on is ApplyImmediateEscalationFlag's job in the same commit.
    private static bool CanStillCommit(Incident incident, EscalationPlan plan)
    {
        if (incident.Status.IsTerminal()) return false;
        if (plan.Immediate) return true;

        return incident.Status != IncidentStatus.Acknowledged && incident.IsEscalationActive;
    }

    /// <summary>After a responder-initiated step, keeps escalation active only while the incident is still Open.</summary>
    // Anywhere else the keypress is a ONE-SHOT. The sweep's predicate excludes Acknowledged but not Investigating, so a flag
    // left standing on an incident somebody took over fires the moment they start investigating.
    private void ApplyImmediateEscalationFlag(Incident incident, EscalationPlan plan)
    {
        if (!plan.Immediate) return;

        var stillEscalating = SweepMayContinue(incident);
        if (incident.IsEscalationActive == stillEscalating) return;

        incident.IsEscalationActive = stillEscalating;

        if (!stillEscalating)
            logger.LogInformation(
                "Responder-initiated escalation for incident {IncidentId} was a one-shot: the incident is {Status}, so the step "
                + "was paged and recorded but escalation is not left active for the sweep.",
                incident.Id, incident.Status);
    }

    /// <summary>Whether the sweep may keep escalating this incident — Open only.</summary>
    // Stricter than the sweep's own predicate on purpose: an acknowledged, investigated or mitigated incident already has an owner.
    private static bool SweepMayContinue(Incident incident) => incident.Status == IncidentStatus.Open;

    /// <summary>Whether <see cref="ProcessPendingEscalationsAsync"/> will pick this incident up again.</summary>
    // Mirrors that method's predicate, and only decides what to tell the operator: "escalation keeps re-trying" has to be true when printed.
    private static bool SweepWillRetry(Incident incident, EscalationPlan plan) =>
        plan.PolicyIsSweepable &&
        incident.IsEscalationActive &&
        !incident.Status.IsTerminal() &&
        incident.Status != IncidentStatus.Acknowledged;

    /// <summary>Dedupe generation for the current escalation run.</summary>
    // Named rather than inlined because a source guard holds every future dispatch path to it.
    internal static long ComputeDispatchGeneration(DateTime? escalationStartedAt, int cyclesCompleted = 0) =>
        NotificationPayload.GenerationFor(escalationStartedAt, cyclesCompleted);

    /// <summary>The step's target, described by name so the timeline reads as prose.</summary>
    // Names come from the loaded navigations; an id is only used when the row it points at is gone,
    // which is still more informative than an id for every target.
    internal static EscalationTarget ResolveTarget(
        EscalationStep step, IReadOnlyDictionary<string, string>? userNames = null)
    {
        var junctionIds = step.TargetedUsers?
            .Select(u => u.UserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        if (junctionIds is { Length: > 0 })
        {
            // Falls back to the id rather than claiming the user is gone: unresolved and removed
            // are different facts, and only one of them is knowable here.
            var named = junctionIds.Select(id =>
                userNames is not null && userNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : id);
            return new EscalationTarget(EscalationTargetKind.Users, UserIds: junctionIds,
                Description: Clip($"Users: {string.Join(", ", named)}"));
        }

        if (step.ScheduleId.HasValue)
            return new EscalationTarget(EscalationTargetKind.Schedule, ScheduleId: step.ScheduleId,
                Description: Clip($"Schedule: {step.Schedule?.Name ?? "a removed schedule"}"));

        if (step.TeamId.HasValue)
            return new EscalationTarget(EscalationTargetKind.Team, TeamId: step.TeamId,
                NotifyAllTeamMembers: step.NotifyAllTeamMembers,
                Description: Clip($"Team: {step.Team?.Name ?? "a removed team"}"
                    + (step.NotifyAllTeamMembers ? " (all)" : " (on-call)")));

        return new EscalationTarget(EscalationTargetKind.None, Description: "(no target)");
    }

    /// <summary>Names channels this deployment has, left untried because responders opted out.</summary>
    // Reads as a configured-but-idle provider otherwise: the operator sets one up, sees no calls,
    // and nothing in the record mentions the preference that suppressed it.
    private static string DescribeOptedOut(NotificationDispatchResult dispatch) =>
        dispatch.OptedOutChannels is not { Count: > 0 }
            ? string.Empty
            : $"Also untried because responders have them switched off in their notification "
                + $"preferences: {dispatch.DescribeOptedOutChannels()}. ";

    private static string Clip(string description) =>
        description.Length <= MaxTargetDescriptionLength
            ? description
            : description[..(MaxTargetDescriptionLength - 1)] + "…";

    private async Task<IReadOnlyDictionary<string, string>> ResolveUserNamesAsync(
        EscalationStep step, CancellationToken cancellationToken)
    {
        var ids = step.TargetedUsers?
            .Select(u => u.UserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToArray();

        if (ids is not { Length: > 0 } || userManager is null) return new Dictionary<string, string>();

        return await userManager.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(
                u => u.Id,
                u => StringExtensions.FormatDisplayName(u.FirstName, u.LastName, u.Email) ?? u.Email ?? u.Id,
                cancellationToken);
    }

    /// <summary>Pages the step's target.</summary>
    // "Nobody paged" is written ONLY when nobody was there to page; a store failure is a different fact with its own record.
    internal async Task<NotificationDispatchResult> DispatchTargetAsync(EscalationPlan plan, CancellationToken cancellationToken)
    {
        var dispatch = plan.Target.Kind switch
        {
            EscalationTargetKind.Users when plan.Target.UserIds is { Length: > 0 } ids =>
                await notificationDispatcher.NotifyUsersAsync(ids, plan.Payload, cancellationToken),

            EscalationTargetKind.Schedule when plan.Target.ScheduleId.HasValue =>
                await notificationDispatcher.NotifyOnCallAsync(plan.Target.ScheduleId.Value, plan.Payload, cancellationToken),

            EscalationTargetKind.Team when plan.Target.TeamId.HasValue =>
                await notificationDispatcher.NotifyTeamAsync(
                    plan.Target.TeamId.Value, plan.Payload, plan.Target.NotifyAllTeamMembers, cancellationToken),

            _ => NotificationDispatchResult.Nobody
        };

        // Three ways to page nobody, three different places to send the operator. Only the first is an
        // empty rota. The other two are NOT mutually exclusive — a step can page a schedule whose
        // primary has no phone number AND whose secondary's voice provider is missing — so both are
        // written when both are true, rather than one silently shadowing the other.
        if (dispatch.NobodyToPage)
        {
            await WriteNobodyReachedTimelineAsync(plan, cancellationToken);
        }
        else
        {
            if (dispatch.ChannelsSilent)
                await WriteChannelsSilentTimelineAsync(plan, dispatch, cancellationToken);

            if (dispatch.TargetsUnpageable)
                await WriteTargetsUnpageableTimelineAsync(plan, dispatch, cancellationToken);
        }

        return dispatch;
    }

    /// <summary>Records that responders were on call but not one had a channel that could page a human.</summary>
    // Kept distinct from an empty rota: the fault is a contact profile, and "no on-call responder" sends the operator to hunt the schedule instead.
    private async Task WriteTargetsUnpageableTimelineAsync(
        EscalationPlan plan, NotificationDispatchResult dispatch, CancellationToken cancellationToken)
    {
        // Same rule as the empty rota: nothing is in flight, so no ack can arrive and there is nothing
        // for this step's delay to wait for.
        var next = plan.Target.Kind is EscalationTargetKind.Schedule or EscalationTargetKind.Team
            ? "The escalation will advance to the next step on the next tick."
            : "The escalation will advance to the next step once that step's delay has elapsed.";

        logger.LogError(
            "Escalation step {Level} for incident {IncidentId} paged NOBODY: {Unpageable} responder(s) are on call, but not one "
            + "of them has a channel that can page a human ({Targets}). The rota is not the problem — their contact profiles are.",
            plan.Step.Level, plan.IncidentId, dispatch.Unpageable, dispatch.DescribeUnpageableTargets());

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = plan.IncidentId,
                EventType = TimelineEventType.Escalated,
                Title = $"Escalation step {plan.Step.Level}: nobody could be paged (the responder has no channel that can page them)",
                Description =
                    $"{plan.Target.Description} — {dispatch.Unpageable} responder(s) are on call, but nothing we have can wake "
                    + $"them: {dispatch.DescribeUnpageableTargets()}. "
                    + "This is NOT an empty on-call rota — the schedule is fine and somebody is on it. The fix is on their side, "
                    + "and the reason(s) above say which: add a phone number, enable a paging channel, or — if the account no "
                    + $"longer exists — put a real responder on the rota. (Push alone cannot page anyone.) {next}",
                ActorUserId = "system"
            }, cancellationToken);

            await auditLogService.LogAsync(
                EscalationActor, AuditAction.EscalationTargetsUnpageable, "Incident", plan.IncidentId.ToString(),
                null,
                $"Step {plan.Step.Level} ({plan.Target.Kind}): {dispatch.Unpageable} target(s) on call with no channel that can page them [{dispatch.DescribeUnpageableTargets()}]",
                cancellationToken: cancellationToken);

            return true;
        }, cancellationToken);
    }

    /// <summary>Records that there was somebody to page and no channel of theirs could send.</summary>
    // Its own record, not the empty-rota one: the schedule is fine and a provider or SMTP is not. The row's own error text is
    // carried through rather than summarised, so the reason is on the incident at 3am instead of in the logs.
    private async Task WriteChannelsSilentTimelineAsync(
        EscalationPlan plan, NotificationDispatchResult dispatch, CancellationToken cancellationToken)
    {
        // Same rule as the empty-rota case, and it must stay the same: nothing is in flight, so no ack
        // can arrive and there is nothing for this step's delay to wait FOR. Serving out the delay
        // would only postpone the next step — the one that might have a channel that works.
        var next = plan.Target.Kind is EscalationTargetKind.Schedule or EscalationTargetKind.Team
            ? "The escalation will advance to the next step on the next tick."
            : "The escalation will advance to the next step once that step's delay has elapsed.";

        logger.LogError(
            "Escalation step {Level} for incident {IncidentId} paged NOBODY: {Silent} target(s) were on-call and reachable on paper, "
            + "but every one of their channels sent nothing ({Silences}). The rota is not the problem — the channels are not configured.",
            plan.Step.Level, plan.IncidentId, dispatch.Silent, dispatch.DescribeChannelSilences());

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = plan.IncidentId,
                EventType = TimelineEventType.Escalated,
                Title = $"Escalation step {plan.Step.Level}: nobody could be paged (no channel could send)",
                Description =
                    $"{plan.Target.Description} — {dispatch.Silent} responder(s) were on-call, but not one of their notification "
                    + $"channels could send, so nobody was contacted: {dispatch.DescribeChannelSilences()}. "
                    + $"This is NOT an empty on-call rota — the responders are there and the schedule is fine. "
                    + $"Configure the channel(s) above (a voice provider, SMTP) or nobody on this step can be paged. "
                    + DescribeOptedOut(dispatch) + next,
                ActorUserId = "system"
            }, cancellationToken);

            await auditLogService.LogAsync(
                EscalationActor, AuditAction.EscalationChannelsSilent, "Incident", plan.IncidentId.ToString(),
                null,
                $"Step {plan.Step.Level} ({plan.Target.Kind}): {dispatch.Silent} target(s) on-call but no channel could send [{dispatch.DescribeChannelSilences()}]",
                cancellationToken: cancellationToken);

            return true;
        }, cancellationToken);
    }

    private async Task WriteNobodyReachedTimelineAsync(EscalationPlan plan, CancellationToken cancellationToken)
    {
        var reason = plan.Target.Kind switch
        {
            EscalationTargetKind.Schedule => $"{plan.Target.Description} — no on-call responder.",
            EscalationTargetKind.Team => plan.Target.NotifyAllTeamMembers
                ? $"{plan.Target.Description} — no reachable members."
                : $"{plan.Target.Description} — nobody currently on-call.",
            // NOT this branch: an on-call responder with no usable channel, and an unresolvable targeted user, each get their own
            // record via WriteTargetsUnpageableTimelineAsync. A Users step landing here is the residual empty-target set.
            EscalationTargetKind.Users => "The step's targeted users could not be paged and left no other record.",
            _ => "The step has no notification target configured."
        };

        // Only a schedule/team step with an empty rota is short-circuited to the next tick (see
        // CommitStepAdvanceAsync); every other kind still serves out the next step's delay. Promising
        // "next tick" for those told the operator the escalation was moving on when it was waiting.
        var next = plan.Target.Kind is EscalationTargetKind.Schedule or EscalationTargetKind.Team
            ? "The escalation will advance to the next step on the next tick."
            : "The escalation will advance to the next step once that step's delay has elapsed.";

        EscalationOrchestratorLog.EscalationStepHasNoTarget(logger, plan.Step.Id, plan.IncidentId);

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = plan.IncidentId,
                EventType = TimelineEventType.Escalated,
                Title = $"Escalation step {plan.Step.Level}: nobody paged",
                Description = $"{reason} {next}",
                ActorUserId = "system"
            }, cancellationToken);

            await auditLogService.LogAsync(
                EscalationActor, AuditAction.EscalationNobodyReached, "Incident", plan.IncidentId.ToString(),
                null, $"Step {plan.Step.Level} ({plan.Target.Kind}): {reason}",
                cancellationToken: cancellationToken);

            return true;
        }, cancellationToken);
    }

    private static bool ShouldTriggerStep(DateTime lastTriggerTime, int delayMinutes, DateTime now)
    {
        return Utilities.EscalationCalculations.ShouldTriggerStep(lastTriggerTime, delayMinutes, now);
    }

    private static int ClampedMaxCycles(EscalationPolicy policy) =>
        Math.Clamp(policy.MaxRepeatCycles, EscalationPolicy.MinRepeatCycles, EscalationPolicy.MaxRepeatCyclesCap);

    private static int ClampedMaxDurationMinutes(EscalationPolicy policy) =>
        Math.Clamp(
            policy.MaxRepeatDurationMinutes,
            EscalationPolicy.MinRepeatDurationMinutes,
            EscalationPolicy.MaxRepeatDurationMinutesCap);

    private static bool IsRepeatDurationExceeded(Incident incident, EscalationPolicy policy, DateTime now)
    {
        if (policy.ExhaustionBehavior != EscalationExhaustionBehavior.Repeat)
            return false;

        var startedAt = incident.EscalationStartedAt ?? incident.CreatedAt;
        return now - startedAt >= TimeSpan.FromMinutes(ClampedMaxDurationMinutes(policy));
    }

    private static bool TryBeginNextCycle(Incident incident, EscalationPolicy policy, DateTime now)
    {
        if (policy.ExhaustionBehavior != EscalationExhaustionBehavior.Repeat)
            return false;

        if (IsRepeatDurationExceeded(incident, policy, now))
            return false;

        var nextCompleted = incident.EscalationCyclesCompleted + 1;
        if (nextCompleted >= ClampedMaxCycles(policy))
            return false;

        incident.EscalationCyclesCompleted = nextCompleted;
        incident.CurrentEscalationStepId = null;
        incident.LastEscalationStepAt = now;
        incident.IsEscalationActive = true;
        return true;
    }

    private static (string Timeline, string Audit) DescribeExhaustion(
        Incident incident, EscalationPolicy policy, bool currentStepDeleted, DateTime now)
    {
        if (currentStepDeleted)
        {
            return (
                "The active escalation step was deleted and no later step remains, so escalation stopped without acknowledgement.",
                $"Policy {incident.EscalationPolicyId}: active step deleted with no later step to resume");
        }

        if (policy.ExhaustionBehavior == EscalationExhaustionBehavior.Repeat
            && IsRepeatDurationExceeded(incident, policy, now))
        {
            return (
                $"Escalation stopped after {ClampedMaxDurationMinutes(policy)} minutes without acknowledgement.",
                $"Policy {incident.EscalationPolicyId}: max repeat duration reached without ack");
        }

        if (policy.ExhaustionBehavior == EscalationExhaustionBehavior.Repeat
            && incident.EscalationCyclesCompleted >= ClampedMaxCycles(policy))
        {
            return (
                $"Escalation stopped after {ClampedMaxCycles(policy)} full passes without acknowledgement.",
                $"Policy {incident.EscalationPolicyId}: max repeat cycles reached without ack");
        }

        return (
            "Escalation policy reached its final step without acknowledgement.",
            $"Policy {incident.EscalationPolicyId} reached final step without ack");
    }

    public async Task TriggerEscalationAsync(Guid incidentId, Guid policyId, CancellationToken cancellationToken = default)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = await incidentRepo.GetByIdAsync(incidentId, cancellationToken);
            if (incident == null) return false;

            if (incident.Status == IncidentStatus.Acknowledged ||
                incident.Status == IncidentStatus.Resolved ||
                incident.Status == IncidentStatus.Closed)
            {
                EscalationOrchestratorLog.EscalationTriggered(logger, incidentId, policyId);
                return true;
            }

            if (incident.IsEscalationActive && incident.EscalationPolicyId == policyId)
            {
                return true;
            }

            if (!incident.IsEscalationActive &&
                incident.CurrentEscalationStepId is not null &&
                incident.EscalationPolicyId == policyId)
            {
                logger.LogWarning(
                    "TriggerEscalation ignored for incident {IncidentId}: escalation was deactivated " +
                    "but step {Step} already ran. Likely a stale message replay.",
                    incidentId, incident.CurrentEscalationStepId);
                return true;
            }

            incident.EscalationPolicyId = policyId;
            incident.EscalationStartedAt = DateTime.UtcNow;
            incident.IsEscalationActive = true;
            incident.CurrentEscalationStepId = null;
            incident.LastEscalationStepAt = null;
            incident.EscalationCyclesCompleted = 0;
            // A new run starts with a clean retry window; a marker left by the previous run would
            // otherwise make this run's first failed step give up immediately.
            incident.DispatchFailingSince = null;

            EscalationOrchestratorLog.EscalationTriggered(logger, incidentId, policyId);
            return true;
        }, cancellationToken);
    }

    public async Task CancelEscalationAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = await incidentRepo.GetByIdAsync(incidentId, cancellationToken);
            if (incident == null) return false;

            incident.IsEscalationActive = false;

            EscalationOrchestratorLog.EscalationCancelled(logger, incidentId);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> AdvanceEscalationAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = await incidentRepo.GetByIdAsync(incidentId, cancellationToken);
            if (incident == null) return false;

            if (incident.Status.IsTerminal() || incident.Status == IncidentStatus.Acknowledged)
            {
                logger.LogInformation(
                    "AdvanceEscalation ignored for incident {IncidentId}: status is {Status}",
                    incidentId, incident.Status);
                return false;
            }

            if (!incident.IsEscalationActive || incident.EscalationPolicyId is null)
            {
                logger.LogInformation(
                    "AdvanceEscalation ignored for incident {IncidentId}: no active escalation",
                    incidentId);
                return false;
            }

            incident.LastEscalationStepAt = DateTime.UtcNow.AddDays(-1);
            incident.UpdatedAt = DateTime.UtcNow;

            logger.LogInformation(
                "AdvanceEscalation queued for incident {IncidentId} (current step {Step})",
                incidentId, incident.CurrentEscalationStepId);
            return true;
        }, cancellationToken);
    }

    internal enum EscalationTargetKind { None, Users, Schedule, Team }

    internal sealed record EscalationTarget(
        EscalationTargetKind Kind,
        string[]? UserIds = null,
        Guid? ScheduleId = null,
        Guid? TeamId = null,
        bool NotifyAllTeamMembers = false,
        string Description = "");

    // FailingSince: when this step's page first failed to be queued, so the retry window counts failing time, not overdue time.
    // Immediate: asked for by a responder on the phone, not the sweep. PolicyIsSweepable: only affects what a failed dispatch reports.
    internal sealed record EscalationPlan(
        Guid IncidentId,
        EscalationStep Step,
        NotificationPayload Payload,
        EscalationTarget Target,
        DateTime? FailingSince = null,
        bool Immediate = false,
        bool PolicyIsSweepable = true);

    /// <summary>True while a call placed for this incident is still ringing or connected.</summary>
    private async Task<bool> HasPageStillInFlightAsync(Guid incidentId, DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now - InFlightPageWindow;

        return await callLogRepo.GetQueryable()
            .AsNoTracking()
            .AnyAsync(
                c => c.IncidentId == incidentId
                     && (c.Status == CallStatus.Initiated || c.Status == CallStatus.Connected)
                     && c.InitiatedAt >= cutoff,
                cancellationToken);
    }
}
