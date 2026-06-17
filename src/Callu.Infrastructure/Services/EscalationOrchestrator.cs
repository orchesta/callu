using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Notifications;
using Callu.Infrastructure.Telemetry;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Orchestrates incident escalation through policy steps.
/// Each incident is advanced in its own transaction: the incident state change
/// (step pointer, last-step timestamp, IsEscalationActive) is committed BEFORE
/// notifications are dispatched, so a crash mid-run cannot produce duplicate
/// notifications for the same step. Dispatch failures are logged and the
/// notification pipeline's own retry/backoff handles redelivery.
/// </summary>
public class EscalationOrchestrator(
    IIncidentRepository incidentRepo,
    IIncidentTimelineEventRepository timelineRepo,
    IAuditLogService auditLogService,
    ITransactionManager transactionManager,
    INotificationDispatcher notificationDispatcher,
    CalluMetrics metrics,
    ILogger<EscalationOrchestrator> logger) : IEscalationOrchestrator
{
    private const int MinDelayMinutesBetweenSteps = 2;

    public async Task ProcessPendingEscalationsAsync(CancellationToken cancellationToken = default)
    {
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

    /// <summary>
    /// Advance a single incident. The state change is committed first; notifications
    /// are dispatched only after the transaction succeeds.
    /// </summary>
    private async Task ProcessOneIncidentAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        var plan = await transactionManager.ExecuteInTransactionAsync<EscalationPlan?>(async () =>
        {
            var incident = await incidentRepo.GetQueryable()
                .Include(i => i.EscalationPolicy)
                    .ThenInclude(p => p!.Steps)
                        .ThenInclude(s => s.TargetedUsers)
                .Include(i => i.CurrentEscalationStep)
                .Include(i => i.Service)
                .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

            if (incident?.EscalationPolicy == null) return null;

            if (incident.Status.IsTerminal() || incident.Status == IncidentStatus.Acknowledged)
                return null;

            var steps = incident.EscalationPolicy.Steps
                .Where(s => !s.IsDeleted)
                .OrderBy(s => s.Level)
                .ThenBy(s => s.CreatedAt)
                .ThenBy(s => s.Id)
                .ToList();

            if (steps.Count == 0) return null;

            var now = DateTime.UtcNow;
            EscalationStep? nextStep;
            bool exhausted = false;

            if (incident.CurrentEscalationStepId == null)
            {
                nextStep = steps[0];

                var startedAt = incident.EscalationStartedAt;
                if (startedAt is null)
                {
                    EscalationOrchestratorLog.EscalationTimestampMissing(logger, incident.Id,
                        nameof(Incident.EscalationStartedAt), nameof(Incident.CreatedAt));
                    startedAt = incident.CreatedAt;
                    incident.EscalationStartedAt = startedAt;
                }

                if (!ShouldTriggerStep(startedAt.Value, nextStep.DelayMinutes, now))
                    return null;
            }
            else
            {
                var currentStepIndex = steps.FindIndex(s => s.Id == incident.CurrentEscalationStepId);
                if (currentStepIndex < 0 || currentStepIndex >= steps.Count - 1)
                {
                    incident.IsEscalationActive = false;
                    exhausted = true;
                    nextStep = null;
                    EscalationOrchestratorLog.EscalationCompleted(logger, incident.Id);
                }
                else
                {
                    nextStep = steps[currentStepIndex + 1];

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

            if (exhausted)
            {
                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incident.Id,
                    EventType = TimelineEventType.Escalated,
                    Title = "Escalation exhausted",
                    Description = "Escalation policy reached its final step without acknowledgement.",
                    ActorUserId = "system"
                }, cancellationToken);

                await auditLogService.LogAsync(
                    "system", "EscalationExhausted", "Incident", incident.Id.ToString(),
                    null, $"Policy {incident.EscalationPolicyId} reached final step without ack",
                    cancellationToken);

                logger.LogError(
                    "Escalation policy {PolicyId} for incident {IncidentId} reached its final step without acknowledgement — no further escalation will occur. Configure a fallback step.",
                    incident.EscalationPolicyId, incident.Id);

                return null;
            }

            if (nextStep == null) return null;

            incident.CurrentEscalationStepId = nextStep.Id;
            incident.LastEscalationStepAt = now;

            var target = ResolveTarget(nextStep);

            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = incident.Id,
                EventType = TimelineEventType.Escalated,
                Title = $"Escalation step {nextStep.Level} triggered",
                Description = target.Description,
                ActorUserId = "system"
            }, cancellationToken);

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
                    IncludeSecondaryOnCall = nextStep.NotifyBothOnCall
                },
                Target: target);
        }, cancellationToken);

        if (plan == null) return;

        metrics.EscalationStepTriggered(plan.Step.Level);
        EscalationOrchestratorLog.TriggeringEscalationStep(logger, plan.Step.Level, plan.IncidentId);

        await DispatchTargetAsync(plan, cancellationToken);
    }

    internal static EscalationTarget ResolveTarget(EscalationStep step)
    {
        var junctionIds = step.TargetedUsers?
            .Select(u => u.UserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        if (junctionIds is { Length: > 0 })
        {
            return new EscalationTarget(EscalationTargetKind.Users, UserIds: junctionIds,
                Description: $"Users: {string.Join(",", junctionIds)}");
        }

        if (step.ScheduleId.HasValue)
            return new EscalationTarget(EscalationTargetKind.Schedule, ScheduleId: step.ScheduleId,
                Description: $"Schedule: {step.ScheduleId}");

        if (step.TeamId.HasValue)
            return new EscalationTarget(EscalationTargetKind.Team, TeamId: step.TeamId,
                NotifyAllTeamMembers: step.NotifyAllTeamMembers,
                Description: $"Team: {step.TeamId}{(step.NotifyAllTeamMembers ? " (all)" : " (on-call)")}");

        return new EscalationTarget(EscalationTargetKind.None, Description: "(no target)");
    }

    internal async Task DispatchTargetAsync(EscalationPlan plan, CancellationToken cancellationToken)
    {
        var reached = plan.Target.Kind switch
        {
            EscalationTargetKind.Users when plan.Target.UserIds is { Length: > 0 } ids =>
                await notificationDispatcher.NotifyUsersAsync(ids, plan.Payload, cancellationToken),

            EscalationTargetKind.Schedule when plan.Target.ScheduleId.HasValue =>
                await notificationDispatcher.NotifyOnCallAsync(plan.Target.ScheduleId.Value, plan.Payload, cancellationToken),

            EscalationTargetKind.Team when plan.Target.TeamId.HasValue =>
                await notificationDispatcher.NotifyTeamAsync(
                    plan.Target.TeamId.Value, plan.Payload, plan.Target.NotifyAllTeamMembers, cancellationToken),

            _ => 0
        };

        if (reached == 0)
            await WriteNobodyReachedTimelineAsync(plan, cancellationToken);
    }

    private async Task WriteNobodyReachedTimelineAsync(EscalationPlan plan, CancellationToken cancellationToken)
    {
        var reason = plan.Target.Kind switch
        {
            EscalationTargetKind.Schedule => $"Schedule {plan.Target.ScheduleId} has no on-call responder.",
            EscalationTargetKind.Team => plan.Target.NotifyAllTeamMembers
                ? $"Team {plan.Target.TeamId} has no reachable members."
                : $"Team {plan.Target.TeamId} has nobody currently on-call.",
            EscalationTargetKind.Users => "None of the step's targeted users could be reached (missing contacts or all channels disabled).",
            _ => "The step has no notification target configured."
        };

        EscalationOrchestratorLog.EscalationStepHasNoTarget(logger, plan.Step.Id, plan.IncidentId);

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = plan.IncidentId,
                EventType = TimelineEventType.Escalated,
                Title = $"Escalation step {plan.Step.Level}: nobody paged",
                Description = $"{reason} The escalation will advance to the next step on the next tick.",
                ActorUserId = "system"
            }, cancellationToken);

            await auditLogService.LogAsync(
                "system", "EscalationNobodyReached", "Incident", plan.IncidentId.ToString(),
                null, $"Step {plan.Step.Level} ({plan.Target.Kind}): {reason}",
                cancellationToken);

            if (plan.Target.Kind is EscalationTargetKind.Schedule or EscalationTargetKind.Team)
            {
                var incident = await incidentRepo.GetByIdAsync(plan.IncidentId, cancellationToken);
                if (incident is { IsEscalationActive: true })
                    incident.LastEscalationStepAt = DateTime.UtcNow.AddMinutes(-(MinDelayMinutesBetweenSteps + 1));
            }
            return true;
        }, cancellationToken);
    }

    private static bool ShouldTriggerStep(DateTime lastTriggerTime, int delayMinutes, DateTime now)
    {
        return Utilities.EscalationCalculations.ShouldTriggerStep(lastTriggerTime, delayMinutes, now);
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

    internal sealed record EscalationPlan(
        Guid IncidentId,
        EscalationStep Step,
        NotificationPayload Payload,
        EscalationTarget Target);
}
