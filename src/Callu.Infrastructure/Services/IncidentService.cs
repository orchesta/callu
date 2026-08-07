using Callu.Shared.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FluentValidation;
using Mapster;
using Callu.Application.Services;
using Callu.Application.Plugins;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Shared.Models.Incidents;
using Callu.Shared.Models.Notifications;
using Callu.Shared.Extensions;
using Callu.Shared.Results;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Exceptions;
using Callu.Application.Common.Interfaces;
using Callu.Application.Messaging;
using Callu.Infrastructure.Telemetry;
using Callu.Infrastructure.Utilities;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Incident command service — handles CRUD and workflow operations.
/// Dashboard queries and analytics are in IncidentQueryService.
/// </summary>
public class IncidentService(
    IIncidentRepository incidentRepo,
    IIncidentTimelineEventRepository timelineRepo,
    IEscalationPolicyRepository escalationPolicyRepo,
    ITeamMemberRepository teamMemberRepo,
    ICallLogRepository callLogRepo,
    IRepository<WebhookDelivery> webhookDeliveryRepo,
    IRepository<ConferenceRoom> conferenceRoomRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    IValidator<CreateIncidentRequest> createValidator,
    IIncidentEventDispatcher eventDispatcher,
    IEscalationOrchestrator escalationOrchestrator,
    IEscalationWorkflowSignal escalationWorkflow,
    IAlertRuleEngine alertRuleEngine,
    IAuditLogService auditLogService,
    ICurrentUserService currentUser,
    IServiceRepository serviceRepo,
    INotificationChannelService notificationChannelService,
    IMaintenanceWindowService maintenanceWindowService,
    CalluMetrics metrics,
    ILogger<IncidentService> logger,
    INotificationPushService? pushService = null,
    IOrganizationSettingsRepository? organizationSettings = null) : IIncidentService
{
    public async Task<IEnumerable<IncidentListItemDto>> GetIncidentsAsync(IncidentFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var query = await BuildBaseQueryAsync(cancellationToken);
        query = ApplyFilters(query, filter);
        
        var incidents = await query
            .OrderByDescending(i => i.StartedAt)
            .ToListAsync(cancellationToken);
        
        return await MapWithUserNamesAsync(incidents);
    }

    public async Task<PagedResult<IncidentListItemDto>> GetIncidentsPagedAsync(IncidentFilter filter, CancellationToken cancellationToken = default)
    {
        var query = await BuildBaseQueryAsync(cancellationToken);
        query = ApplyFilters(query, filter);

        var totalCount = await query.CountAsync(cancellationToken);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        
        var incidents = await query
            .OrderByDescending(i => i.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<IncidentListItemDto>.Create(
            await MapWithUserNamesAsync(incidents), totalCount, page, pageSize);
    }

    public async Task<IncidentDto?> GetIncidentByIdAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        var baseQuery = await BuildBaseQueryAsync(cancellationToken);
        var incident = await baseQuery
            .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

        if (incident == null) return null;
        
        var dto = incident.Adapt<IncidentDto>();
        
        var userIds = new[] { incident.AcknowledgedBy, incident.ResolvedBy }
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();
        
        if (userIds.Any())
        {
            var userNames = await ResolveUserNamesAsync(userIds!);
            
            dto = dto with
            {
                AcknowledgedBy = !string.IsNullOrEmpty(incident.AcknowledgedBy) && userNames.TryGetValue(incident.AcknowledgedBy, out var ackName) ? ackName : null,
                ResolvedBy = !string.IsNullOrEmpty(incident.ResolvedBy) && userNames.TryGetValue(incident.ResolvedBy, out var resName) ? resName : null
            };
        }
        
        return dto;
    }

    public async Task<IncidentCreateResult> CreateIncidentAsync(CreateIncidentRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new FluentValidation.ValidationException(validationResult.Errors);
        }

        string? maintenanceMode = null;
        if (request.ServiceId.HasValue && request.ServiceId.Value != Guid.Empty)
        {
            maintenanceMode = await maintenanceWindowService.GetMaintenanceModeForServiceAsync(
                request.ServiceId.Value, cancellationToken);

            if (maintenanceMode == nameof(MaintenanceWindowMode.SuppressAlerts))
            {
                logger.LogInformation(
                    "Incident suppressed by maintenance window for service {ServiceId}: {Title}",
                    request.ServiceId, request.Title);
                await auditLogService.LogAsync(
                    MaintenanceActor, AuditAction.Suppressed, "Service", request.ServiceId.Value.ToString(),
                    null, $"Service in maintenance (SuppressAlerts): {request.Title}",
                    description: $"Alert suppressed, no incident created: {request.Title}",
                    cancellationToken: cancellationToken);
                return new IncidentCreateResult
                {
                    Outcome = IncidentCreateOutcome.Suppressed,
                    Incident = null,
                    Reason = "Service is in a maintenance window with SuppressAlerts mode."
                };
            }
        }
        var autoAcknowledge = maintenanceMode == nameof(MaintenanceWindowMode.AutoAcknowledge);
        
        (IncidentDto Dto, IEscalationDispatchHandle Handle, Guid IncidentId, Guid? PolicyId) staged;
        try
        {
            staged = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = request.Adapt<Incident>();

            if (!incident.TeamId.HasValue && incident.ServiceId != Guid.Empty)
            {
                var service = await serviceRepo.FindSingleAsync(s => s.Id == incident.ServiceId && !s.IsDeleted, cancellationToken);
                if (service?.TeamId != null)
                {
                    incident.TeamId = service.TeamId;
                }
            }

            if (!string.IsNullOrEmpty(incident.ExternalAlertId))
            {
                var exists = await incidentRepo.GetQueryable()
                    .AnyAsync(i => i.ServiceId == incident.ServiceId
                                && i.ExternalAlertId == incident.ExternalAlertId
                                && i.Status != IncidentStatus.Resolved
                                && i.Status != IncidentStatus.Closed
                                && !i.IsDeleted, cancellationToken);
                if (exists)
                    throw new ConflictException($"An active incident with ExternalAlertId '{incident.ExternalAlertId}' already exists.");
            }

            incident.Title = incident.Title.Trim();
            incident.Description = incident.Description?.Trim();
            incident.DataLanguage = string.IsNullOrEmpty(request.DataLanguage)
                ? await DefaultDataLanguageAsync(cancellationToken)
                : request.DataLanguage;

            await incidentRepo.AddAsync(incident, cancellationToken);

            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = incident.Id,
                EventType = TimelineEventType.Created,
                Title = "Incident created",
                Description = $"[{incident.Severity}] {incident.Title}",
                ActorUserId = currentUser.UserId ?? "system"
            }, cancellationToken);

            await auditLogService.LogAsync(
                Actor(), AuditAction.Created, "Incident", incident.Id.ToString(),
                null, System.Text.Json.JsonSerializer.Serialize(request),
                description: $"[{incident.Severity}] {incident.Title}", cancellationToken: cancellationToken);

            IncidentServiceLog.IncidentCreated(logger, incident.Id);

            if (autoAcknowledge)
            {
                incident.Acknowledge(MaintenanceActor);
                await auditLogService.LogAsync(
                    MaintenanceActor, AuditAction.Acknowledged, "Incident", incident.Id.ToString(),
                    "Status: Open", "Status: Acknowledged",
                    description: "Acknowledged automatically: the service is in a maintenance window",
                    cancellationToken: cancellationToken);
                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incident.Id,
                    EventType = TimelineEventType.Acknowledged,
                    Title = "Auto-acknowledged",
                    Description = "Service is in a maintenance window (AutoAcknowledge).",
                    ActorUserId = "system:maintenance"
                }, cancellationToken);
            }

            IEscalationDispatchHandle escalationHandle = NoOpEscalationDispatchHandle.Instance;
            Guid? stagedPolicyId = null;
            if (!autoAcknowledge && !incident.TeamId.HasValue)
            {
                await RecordNobodyCanBePagedAsync(incident.Id,
                    "No team is assigned to this incident, so no escalation policy applies and nobody was paged. "
                    + "Assign a team, or set one on the service so future alerts inherit it.",
                    "no team assigned", cancellationToken);
            }

            if (!autoAcknowledge && incident.TeamId.HasValue)
            {
                var policy = await escalationPolicyRepo.GetActiveForTeamAsync(incident.TeamId.Value, cancellationToken);
                if (policy == null)
                {
                    await RecordNobodyCanBePagedAsync(incident.Id,
                        "The assigned team has no active escalation policy, so nobody was paged. "
                        + "Create a policy for the team and enable it.",
                        "team has no active escalation policy", cancellationToken);
                }
                else
                {
                    // Opt-in pre-pass: a SuppressNotification rule with SuppressPaging skips escalation staging entirely.
                    // Fails OPEN — if the pre-pass itself throws, paging proceeds; paging by accident beats silently not paging.
                    string? suppressingRule = null;
                    try
                    {
                        suppressingRule = await alertRuleEngine.ShouldSuppressPagingAsync(incident, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Paging-suppression pre-pass failed for incident {IncidentId}; proceeding with escalation staging",
                            incident.Id);
                    }

                    if (suppressingRule is not null)
                    {
                        incident.IsPagingSuppressed = true;
                        incident.IsNotificationSuppressed = true; // suppressing pages implies suppressing channel posts
                        await timelineRepo.AddAsync(new IncidentTimelineEvent
                        {
                            IncidentId = incident.Id,
                            EventType = TimelineEventType.NoteAdded,
                            Title = "Paging suppressed",
                            Description = $"Paging suppressed by alert rule '{suppressingRule}' — automatic escalation was not started.",
                            ActorUserId = "system:alert-rule"
                        }, cancellationToken);
                        logger.LogInformation(
                            "Paging suppressed by alert rule '{RuleName}' for incident {IncidentId}; escalation not staged",
                            suppressingRule, incident.Id);
                    }
                    else
                    {
                        escalationHandle = await escalationWorkflow.StageForNewIncidentAsync(
                            incident.Id, policy.Id, cancellationToken);
                        stagedPolicyId = policy.Id;
                        incident.EscalationStagedAt = DateTime.UtcNow;
                    }
                }
            }

            return (incident.Adapt<IncidentDto>(), escalationHandle, IncidentId: incident.Id, PolicyId: stagedPolicyId);
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw new ConflictException(
                $"An active incident with ExternalAlertId '{request.ExternalAlertId}' already exists.");
        }

        var (dto, escalationHandle, escalationIncidentId, escalationPolicyId) = staged;

        try
        {
            // Post-commit side effect: not bound to the request token, and a failure here
            // must not fail a create that already committed. ReconcileUnactivatedEscalations
            // picks the incident back up via EscalationStagedAt.
            await escalationHandle.DispatchAsync(CancellationToken.None);
            if (escalationPolicyId.HasValue)
                IncidentServiceLog.EscalationAutoTriggered(logger, escalationIncidentId, escalationPolicyId.Value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Escalation dispatch failed for incident {IncidentId}", escalationIncidentId);
            try
            {
                await auditLogService.LogAsync(
                    DispatchActor, AuditAction.DispatchFailed, "Incident", escalationIncidentId.ToString(),
                    null, ex.Message,
                    description: "Escalation dispatch failed", cancellationToken: cancellationToken);
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx, "Failed to persist DispatchFailed audit for incident {IncidentId}", escalationIncidentId);
            }
        }

        metrics.IncidentCreated(dto.Severity);

        try
        {
            var persisted = await incidentRepo.GetByIdAsync(dto.Id, cancellationToken);
            if (persisted != null)
            {
                var triggered = await alertRuleEngine.EvaluateAsync(persisted, cancellationToken);
                if (triggered > 0)
                    logger.LogInformation("Alert rules triggered {Count} action(s) for incident {IncidentId}", triggered, dto.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Alert-rule evaluation failed for incident {IncidentId}", dto.Id);
        }

        try
        {
            var suppress = await incidentRepo.GetQueryable()
                .AsNoTracking()
                .Where(i => i.Id == dto.Id)
                .Select(i => i.IsNotificationSuppressed)
                .FirstOrDefaultAsync(cancellationToken);

            if (!suppress)
            {
                await notificationChannelService.DispatchIncidentNotificationAsync(
                    dto.Id,
                    dto.Title,
                    dto.Severity,
                    request.ServiceId,
                    NotificationChannelDispatchEvent.IncidentCreated,
                    cancellationToken);
            }
            else
            {
                logger.LogInformation("Channel notifications suppressed by alert-rule for incident {IncidentId}", dto.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Channel notification dispatch failed for incident {IncidentId}", dto.Id);
            try
            {
                await auditLogService.LogAsync(
                    DispatchActor, AuditAction.DispatchFailed, "Incident", dto.Id.ToString(),
                    null, $"IncidentCreated: {ex.Message}",
                    description: "Channel notification dispatch failed", cancellationToken: cancellationToken);
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx, "Failed to persist DispatchFailed audit for incident {IncidentId}", dto.Id);
            }
        }

        if (autoAcknowledge)
            logger.LogInformation(
                "Incident {IncidentId} auto-acknowledged at creation by maintenance window for service {ServiceId}",
                dto.Id, request.ServiceId);

        await BroadcastLifecycleAsync(dto.Id, autoAcknowledge ? "Acknowledged" : "Open", cancellationToken);
        return new IncidentCreateResult { Outcome = IncidentCreateOutcome.Created, Incident = dto };
    }

    public async Task UpdateIncidentAsync(Guid incidentId, UpdateIncidentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = await FindIncidentForMutationAsync(incidentId, cancellationToken);

            // Read before anything is applied: this endpoint can drive every lifecycle transition
            // and used to leave neither an audit row nor a timeline event behind.
            var before = Snapshot(incident);

            if (!string.IsNullOrWhiteSpace(request.Title))
                incident.Title = request.Title.Trim();

            if (request.Description != null)
                incident.Description = request.Description.Trim();

            if (!string.IsNullOrWhiteSpace(request.Severity) &&
                Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity))
                incident.Severity = severity;

            if (request.ServiceId.HasValue)
                incident.ServiceId = request.ServiceId.Value;

            if (request.TeamId.HasValue)
                incident.TeamId = request.TeamId.Value;

            var statusBefore = incident.Status;
            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<IncidentStatus>(request.Status, ignoreCase: true, out var target))
            {
                var actor = currentUser.UserId ?? SystemCurrentUserService.SystemUserId;
                try
                {
                    incident.ChangeStatus(target, actor);
                }
                catch (InvalidOperationException ex)
                {
                    throw new ConflictException(ex.Message);
                }
            }

            incident.UpdatedAt = DateTime.UtcNow;

            // A transition driven through here has to produce the same action the dedicated
            // endpoint would, or the trail says two different things about one kind of act.
            if (incident.Status != statusBefore)
            {
                await auditLogService.LogAsync(
                    Actor(), TransitionAction(incident.Status), "Incident", incidentId.ToString(),
                    $"Status: {statusBefore}", $"Status: {incident.Status}",
                    description: $"Status changed from {statusBefore} to {incident.Status}",
                    cancellationToken: cancellationToken);

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incidentId,
                    EventType = TimelineEventType.StatusChanged,
                    Title = $"{incident.Status}",
                    Description = $"Status changed from {statusBefore} to {incident.Status}",
                    ActorUserId = Actor()
                }, cancellationToken);
            }

            var changed = Diff(before, Snapshot(incident));
            if (changed.Count > 0)
            {
                await auditLogService.LogAsync(
                    Actor(), AuditAction.Updated, "Incident", incidentId.ToString(),
                    string.Join(", ", changed.Select(c => $"{c.Field}: {c.Before}")),
                    string.Join(", ", changed.Select(c => $"{c.Field}: {c.After}")),
                    description: $"Changed {string.Join(", ", changed.Select(c => c.Field))}",
                    cancellationToken: cancellationToken);
            }

            IncidentServiceLog.IncidentUpdated(logger, incidentId);
            return true;
        }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Incident was modified by another user. Please retry.");
        }

        await BroadcastLifecycleAsync(incidentId, "Updated", cancellationToken);
    }

    public async Task AcknowledgeIncidentAsync(Guid incidentId, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await transactionManager.ExecuteInTransactionAsync(async () =>
            {
                var incident = await FindIncidentForMutationAsync(incidentId, cancellationToken);

                // 409, not 500 — this is one of the two endpoints responders race on.
                try
                {
                    incident.Acknowledge(userId);
                }
                catch (InvalidOperationException ex)
                {
                    throw new ConflictException(ex.Message);
                }

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incidentId,
                    EventType = TimelineEventType.Acknowledged,
                    Title = "Acknowledged",
                    Description = $"Acknowledged by {userId}",
                    ActorUserId = userId
                }, cancellationToken);

                // Unfiltered on purpose: touching every call log of the incident bumps each xmin, which is what
                // serialises this writer against the voice-retry sweep. Clear the stuck marker with the deadline —
                // one without the other misdates the next failure. `CallLog.StandDownRetryChain` must not drift from this.
                await callLogRepo.GetQueryable()
                    .Where(c => c.IncidentId == incidentId)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(c => c.NextRetryAt, (DateTime?)null)
                              .SetProperty(c => c.DialOutFailingSince, (DateTime?)null)
                              .SetProperty(c => c.DialOutFailureKind, (VoiceDialOutFailureKind?)null),
                        cancellationToken);

                IncidentServiceLog.EscalationStoppedAck(logger, incidentId);

                await auditLogService.LogAsync(
                    userId, AuditAction.Acknowledged, "Incident", incidentId.ToString(),
                    "Status: Open", "Status: Acknowledged",
                    description: "Incident acknowledged", cancellationToken: cancellationToken);

                IncidentServiceLog.IncidentAcknowledged(logger, incidentId, userId);
                return true;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            IncidentServiceLog.AckConcurrencyConflict(logger, ex, incidentId);
            throw new ConflictException("Incident was modified by another user. Please retry.");
        }

        await eventDispatcher.SendServiceAckAsync(incidentId, "acknowledge", cancellationToken);

        await TryDispatchOrgNotificationAsync(incidentId, NotificationChannelDispatchEvent.IncidentAcknowledged, cancellationToken);
        await BroadcastLifecycleAsync(incidentId, "Acknowledged", cancellationToken);
    }

    public async Task ResolveIncidentAsync(Guid incidentId, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await transactionManager.ExecuteInTransactionAsync(async () =>
            {
                var incident = await FindIncidentForMutationAsync(incidentId, cancellationToken);

                // Read before the transition, or the audit row says an incident went from Resolved
                // to Resolved and the status it was actually resolved from is lost.
                var previous = incident.Status;

                // 409, not 500 — see the note in AcknowledgeIncidentAsync.
                try
                {
                    incident.Resolve(userId);
                }
                catch (InvalidOperationException ex)
                {
                    throw new ConflictException(ex.Message);
                }

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incidentId,
                    EventType = TimelineEventType.Resolved,
                    Title = "Resolved",
                    Description = $"Resolved by {userId}",
                    ActorUserId = userId
                }, cancellationToken);

                IncidentServiceLog.EscalationStoppedResolution(logger, incidentId);

                await ExpireActiveConferenceRoomsAsync(incidentId, cancellationToken);

                await auditLogService.LogAsync(
                    userId, AuditAction.Resolved, "Incident", incidentId.ToString(),
                    $"Status: {previous}", "Status: Resolved",
                    description: $"Incident resolved from {previous}", cancellationToken: cancellationToken);

                IncidentServiceLog.IncidentResolved(logger, incidentId, userId);
                return true;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            IncidentServiceLog.ResolveConcurrencyConflict(logger, ex, incidentId);
            throw new ConflictException("Incident was modified by another user. Please retry.");
        }

        await eventDispatcher.SendServiceAckAsync(incidentId, "resolve", cancellationToken);

        await TryDispatchOrgNotificationAsync(incidentId, NotificationChannelDispatchEvent.IncidentResolved, cancellationToken);
        await BroadcastLifecycleAsync(incidentId, "Resolved", cancellationToken);
    }

    public async Task CloseIncidentAsync(Guid incidentId, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await transactionManager.ExecuteInTransactionAsync(async () =>
            {
                var incident = await FindIncidentForMutationAsync(incidentId, cancellationToken);
                var previous = incident.Status;

                incident.Close(userId);

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incidentId,
                    EventType = TimelineEventType.Closed,
                    Title = "Closed",
                    Description = $"Closed by {userId} (from {previous})",
                    ActorUserId = userId
                }, cancellationToken);

                await ExpireActiveConferenceRoomsAsync(incidentId, cancellationToken);

                await auditLogService.LogAsync(
                    userId, AuditAction.Closed, "Incident", incidentId.ToString(),
                    $"Status: {previous}", "Status: Closed",
                    description: $"Incident closed from {previous}", cancellationToken: cancellationToken);
                return true;
            }, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Incident was modified by another user. Please retry.");
        }

        await TryDispatchOrgNotificationAsync(incidentId, NotificationChannelDispatchEvent.IncidentClosed, cancellationToken);
        await BroadcastLifecycleAsync(incidentId, "Closed", cancellationToken);
    }

    public async Task ReopenIncidentAsync(Guid incidentId, string userId, CancellationToken cancellationToken = default)
    {
        IEscalationDispatchHandle escalationHandle = NoOpEscalationDispatchHandle.Instance;
        Guid? stagedPolicyId = null;
        try
        {
            await transactionManager.ExecuteInTransactionAsync(async () =>
            {
                var incident = await FindIncidentForMutationAsync(incidentId, cancellationToken);
                var previous = incident.Status;

                incident.Reopen(userId);

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incidentId,
                    EventType = TimelineEventType.Reopened,
                    Title = "Reopened",
                    Description = $"Reopened from {previous} by {userId}",
                    ActorUserId = userId
                }, cancellationToken);

                // Reopen() cleared the escalation fields, so the marker must describe the new open
                // episode: set when this reopen stages a trigger, cleared when it does not — otherwise
                // a stale marker from the original creation lets Reconcile page an incident whose
                // reopen deliberately staged nothing.
                incident.EscalationStagedAt = null;

                if (incident.TeamId.HasValue)
                {
                    var policy = await escalationPolicyRepo.GetActiveForTeamAsync(incident.TeamId.Value, cancellationToken);
                    if (policy != null)
                    {
                        escalationHandle = await escalationWorkflow.StageForNewIncidentAsync(
                            incident.Id, policy.Id, cancellationToken);
                        stagedPolicyId = policy.Id;
                        incident.EscalationStagedAt = DateTime.UtcNow;
                    }
                }

                await auditLogService.LogAsync(
                    userId, AuditAction.Reopened, "Incident", incidentId.ToString(),
                    $"Status: {previous}", "Status: Open (reopened)",
                    description: $"Incident reopened from {previous}", cancellationToken: cancellationToken);
                return true;
            }, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Incident was modified by another user. Please retry.");
        }

        try
        {
            await escalationHandle.DispatchAsync(CancellationToken.None);
            if (stagedPolicyId.HasValue)
                IncidentServiceLog.EscalationAutoTriggered(logger, incidentId, stagedPolicyId.Value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Escalation dispatch failed for reopened incident {IncidentId}", incidentId);
            try
            {
                await auditLogService.LogAsync(
                    userId, AuditAction.DispatchFailed, "Incident", incidentId.ToString(),
                    null, ex.Message,
                    description: "Escalation dispatch failed on reopen", cancellationToken: cancellationToken);
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx, "Failed to persist DispatchFailed audit for reopened incident {IncidentId}", incidentId);
            }
        }

        await TryDispatchOrgNotificationAsync(incidentId, NotificationChannelDispatchEvent.IncidentReopened, cancellationToken);
        await BroadcastLifecycleAsync(incidentId, "Reopened", cancellationToken);
    }

    /// <summary>
    /// Best-effort SignalR fanout of a lifecycle change; a no-op on the Worker, which has no hub.
    /// </summary>
    private async Task BroadcastLifecycleAsync(Guid incidentId, string status, CancellationToken cancellationToken)
    {
        if (pushService is null) return;
        try
        {
            await pushService.BroadcastIncidentUpdateAsync(incidentId, status, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BroadcastIncidentUpdate failed for {IncidentId} ({Status})", incidentId, status);
        }
    }

    public async Task DeleteIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        try
        {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var tracked = incidentRepo.GetQueryable()
                .Include(i => i.Notes)
                .Include(i => i.TimelineEvents)
                .Include(i => i.Notifications)
                .Include(i => i.CallLogs)
                .Where(i => !i.IsDeleted);

            var scoped = await ApplyTeamScopingAsync(tracked, cancellationToken);
            var incident = await scoped.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)
                ?? throw new NotFoundException("Incident", incidentId);

            var now = DateTime.UtcNow;
            incident.MarkDeleted();
            foreach (var note in incident.Notes)
            {
                note.IsDeleted = true;
                note.UpdatedAt = now;
            }
            foreach (var evt in incident.TimelineEvents)
            {
                evt.IsDeleted = true;
                evt.UpdatedAt = now;
            }
            foreach (var notif in incident.Notifications)
            {
                notif.IsDeleted = true;
                notif.UpdatedAt = now;
            }
            foreach (var call in incident.CallLogs)
            {
                call.IsDeleted = true;
                call.UpdatedAt = now;
            }

            // The one operation that erases the evidence of every other one, so it is the one that
            // most has to leave a row. The counts go in because after this the rows are filtered
            // out of every read and nobody can count them.
            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Deleted, "Incident", incidentId.ToString(),
                $"Status: {incident.Status}, Severity: {incident.Severity}, Title: {incident.Title}",
                null,
                description: $"Incident deleted with {incident.Notes.Count} note(s), "
                    + $"{incident.TimelineEvents.Count} timeline event(s), "
                    + $"{incident.Notifications.Count} notification(s) and {incident.CallLogs.Count} call log(s)",
                cancellationToken: cancellationToken);

            IncidentServiceLog.IncidentDeleted(logger, incidentId);
            return true;
        }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Incident was modified by another user. Please retry.");
        }
    }

    public async Task EscalateIncidentAsync(Guid incidentId, string userId, string? reason = null, CancellationToken cancellationToken = default)
    {
        try
        {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var tracked = incidentRepo.GetQueryable().Where(i => !i.IsDeleted).Include(i => i.Team);
            var scoped = await ApplyTeamScopingAsync(tracked, cancellationToken);
            var incident = await scoped.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)
                ?? throw new NotFoundException("Incident", incidentId);

            if (incident.Status.IsTerminal())
                throw new BusinessRuleException($"Cannot escalate incident in '{incident.Status}' status.");

            var escalated = false;
            if (incident.IsEscalationActive)
            {
                escalated = await escalationOrchestrator.AdvanceEscalationAsync(incident.Id, cancellationToken);
            }
            else if (incident.TeamId.HasValue)
            {
                var policy = await escalationPolicyRepo.GetActiveForTeamAsync(incident.TeamId.Value, cancellationToken);

                if (policy != null)
                {
                    await escalationOrchestrator.TriggerEscalationAsync(incident.Id, policy.Id, cancellationToken);
                    escalated = true;
                }
            }

            if (escalated)
            {
                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incidentId,
                    EventType = TimelineEventType.Escalated,
                    Title = "Escalated",
                    Description = reason ?? "Manually escalated",
                    ActorUserId = userId
                }, cancellationToken);

                incident.UpdatedAt = DateTime.UtcNow;
                await auditLogService.LogAsync(userId, AuditAction.Escalated, "Incident", incidentId.ToString(), null, reason, cancellationToken: cancellationToken);

                IncidentServiceLog.IncidentManuallyEscalated(logger, incidentId, userId);
            }
            else
            {
                logger.LogInformation(
                    "Manual escalate for incident {IncidentId} did nothing (no active escalation and no active team policy).",
                    incidentId);
            }
            return true;
        }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Incident was modified by another user. Please retry.");
        }
    }

    public async Task ReassignIncidentAsync(Guid incidentId, string targetUserId, string assignedBy, CancellationToken cancellationToken = default)
    {
        try
        {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = await FindIncidentForMutationAsync(incidentId, cancellationToken);

            if (incident.Status == IncidentStatus.Resolved || incident.Status == IncidentStatus.Closed)
                throw new BusinessRuleException($"Cannot reassign a {incident.Status} incident. Reopen first.");

            var previousAssignee = incident.AcknowledgedBy;
            incident.AcknowledgedBy = targetUserId;

            if (incident.Status == IncidentStatus.Open)
            {
                incident.Acknowledge(targetUserId);
            }

            incident.UpdatedAt = DateTime.UtcNow;

            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = incidentId,
                EventType = TimelineEventType.Reassigned,
                Title = "Reassigned",
                Description = $"Reassigned from {previousAssignee ?? "unassigned"} to {targetUserId}",
                ActorUserId = assignedBy
            }, cancellationToken);

            await auditLogService.LogAsync(assignedBy, AuditAction.Reassigned, "Incident", incidentId.ToString(), previousAssignee, targetUserId, cancellationToken: cancellationToken);

            IncidentServiceLog.IncidentReassigned(logger, incidentId, targetUserId, assignedBy);
            return true;
        }, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Incident was modified by another user. Please retry.");
        }
    }

    #region Private Helpers

    /// <summary>
    /// Build base incident query with includes, soft-delete filter, and team scoping
    /// </summary>
    private async Task<IQueryable<Incident>> BuildBaseQueryAsync(CancellationToken cancellationToken)
    {
        var query = incidentRepo.GetQueryable()
            .AsNoTracking()
            .Where(i => !i.IsDeleted)
            .Include(i => i.Service)
            .Include(i => i.Team)
            .AsQueryable();

        return await ApplyTeamScopingAsync(query, cancellationToken);
    }

    /// <summary>Fetches a tracked incident for mutation with team scoping applied.</summary>
    // Out of scope and non-existent both throw NotFoundException, so existence is not leaked.
    private async Task<Incident> FindIncidentForMutationAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        var tracked = incidentRepo.GetQueryable().Where(i => !i.IsDeleted);
        var scoped = await ApplyTeamScopingAsync(tracked, cancellationToken);

        return await scoped.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)
            ?? throw new NotFoundException("Incident", incidentId);
    }

    /// <summary>
    /// Apply optional filters (status, severity, service, team, search) to an incident query
    /// </summary>
    private static IQueryable<Incident> ApplyFilters(IQueryable<Incident> query, IncidentFilter? filter)
    {
        if (filter == null) return query;

        if (!string.IsNullOrEmpty(filter.Status) && Enum.TryParse<IncidentStatus>(filter.Status, out var status))
            query = query.Where(i => i.Status == status);
        
        if (!string.IsNullOrEmpty(filter.Severity) &&
            Enum.TryParse<IncidentSeverity>(filter.Severity, ignoreCase: true, out var severity))
            query = query.Where(i => i.Severity == severity);
        
        if (filter.ServiceId is { } serviceId)
            query = query.Where(i => i.ServiceId == serviceId);

        if (filter.TeamId is { } teamId)
            query = query.Where(i => i.TeamId == teamId);
        
        if (!string.IsNullOrEmpty(filter.SearchQuery))
            query = query.Where(i => i.Title.Contains(filter.SearchQuery) || (i.Description != null && i.Description.Contains(filter.SearchQuery)));

        return query;
    }

    /// <summary>
    /// Map incident entities to DTOs with resolved user display names
    /// </summary>
    private async Task<List<IncidentListItemDto>> MapWithUserNamesAsync(List<Incident> incidents)
    {
        var userNames = await ResolveUserNamesAsync(
            incidents.SelectMany(i => new[] { i.AcknowledgedBy, i.ResolvedBy })
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList());
        
        return incidents.Select(i =>
        {
            var dto = i.Adapt<IncidentListItemDto>();
            return dto with
            {
                AcknowledgedBy = !string.IsNullOrEmpty(i.AcknowledgedBy) && userNames.TryGetValue(i.AcknowledgedBy, out var ackName) ? ackName : null,
                ResolvedBy = !string.IsNullOrEmpty(i.ResolvedBy) && userNames.TryGetValue(i.ResolvedBy, out var resName) ? resName : null
            };
        }).ToList();
    }

    /// <summary>
    /// Resolve user IDs to display names via UserManager
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveUserNamesAsync(IList<string?> userIds)
    {
        var distinctIds = userIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct()
            .ToList();

        if (distinctIds.Count == 0)
            return new Dictionary<string, string>();

        var users = await userManager.Users
            .Where(u => distinctIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync();

        return users.ToDictionary(
            u => u.Id,
            u => u.DisplayName ?? u.Email ?? u.Id);
    }

    #endregion

    /// <summary>
    /// Apply team-based data scoping to incident queries.
    /// Admin users see all incidents. Non-admin users see only their team's incidents + unassigned.
    /// </summary>
    private async Task<IQueryable<Incident>> ApplyTeamScopingAsync(IQueryable<Incident> query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.UserId))
            return query;

        // Auditor is exempt for the same reason Admin is: it is a read-only, org-wide role that
        // belongs to no team, so team scoping silently narrows it to nothing and the audit trail
        // ends up referencing incidents its own reader cannot open.
        if (currentUser.IsInRole("Admin") || currentUser.IsInRole("Owner") || currentUser.IsInRole("Auditor"))
            return query;

        var userTeamIds = await teamMemberRepo.GetQueryable()
            .AsNoTracking()
            .Where(tm => tm.UserId == currentUser.UserId)
            .Select(tm => tm.TeamId)
            .ToListAsync(cancellationToken);

        query = query.Where(i => i.TeamId == null || userTeamIds.Contains(i.TeamId.Value));

        return query;
    }

    private async Task TryDispatchOrgNotificationAsync(
        Guid incidentId,
        NotificationChannelDispatchEvent dispatchEvent,
        CancellationToken cancellationToken)
    {
        var snapshot = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .Where(i => i.Id == incidentId && !i.IsDeleted)
            .Select(i => new { i.Title, i.Severity, i.ServiceId, i.IsNotificationSuppressed })
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot == null) return;

        // Review fix: suppression must cover the whole lifecycle, not only Created —
        // otherwise a rule-silenced incident still posts Ack/Resolved/Closed/Reopened
        // to the org channels.
        if (snapshot.IsNotificationSuppressed)
        {
            logger.LogInformation(
                "Channel notifications suppressed by alert-rule for incident {IncidentId} ({Event})",
                incidentId, dispatchEvent);
            return;
        }

        try
        {
            await notificationChannelService.DispatchIncidentNotificationAsync(
                incidentId,
                snapshot.Title,
                snapshot.Severity.ToString(),
                snapshot.ServiceId,
                dispatchEvent,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Channel notification dispatch failed for incident {IncidentId} ({Event})",
                incidentId,
                dispatchEvent);
            try
            {
                await auditLogService.LogAsync(
                    DispatchActor, AuditAction.DispatchFailed, "Incident", incidentId.ToString(),
                    null, $"{dispatchEvent}: {ex.Message}",
                    description: $"Channel notification dispatch failed ({dispatchEvent})", cancellationToken: cancellationToken);
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx, "Failed to persist DispatchFailed audit for incident {IncidentId}", incidentId);
            }
        }
    }

    public async Task<IReadOnlyList<WebhookDeliveryDto>> GetWebhookDeliveriesAsync(
        Guid incidentId, int limit = 20, CancellationToken cancellationToken = default)
    {
        var rows = await webhookDeliveryRepo.GetQueryable()
            .AsNoTracking()
            .Where(d => d.IncidentId == incidentId && !d.IsDeleted)
            .OrderByDescending(d => d.AttemptedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(d => new WebhookDeliveryDto(
                d.Id,
                d.IncidentId,
                d.ServiceId,
                d.Url,
                d.AckType,
                d.HttpStatus,
                d.Error,
                d.AttemptCount,
                d.AttemptedAt,
                d.NextRetryAt,
                d.Status,
                d.ResponseBodySample))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<IncidentEscalationDto?> GetEscalationAsync(
        Guid incidentId, CancellationToken cancellationToken = default)
    {
        var incident = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .Select(i => new
            {
                i.Id,
                i.Status,
                i.CreatedAt,
                i.EscalationPolicyId,
                PolicyName = i.EscalationPolicy!.Name,
                // Nullable casts: an incident with no policy left-joins to nothing, and the
                // shaper cannot put that NULL into a plain enum or int.
                ExhaustionBehavior = (EscalationExhaustionBehavior?)i.EscalationPolicy!.ExhaustionBehavior,
                MaxRepeatCycles = (int?)i.EscalationPolicy!.MaxRepeatCycles,
                i.CurrentEscalationStepId,
                i.EscalationStartedAt,
                i.LastEscalationStepAt,
                i.IsEscalationActive,
                i.EscalationCyclesCompleted,
            })
            .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

        if (incident is null) return null;
        if (incident.EscalationPolicyId is not Guid policyId)
            return new IncidentEscalationDto { RunState = IncidentEscalationRunState.NotConfigured };

        // Same order the orchestrator picks the next step in. Ordering these differently would
        // report a step other than the one about to page.
        var steps = await escalationPolicyRepo.GetQueryable()
            .AsNoTracking()
            .Where(p => p.Id == policyId)
            .SelectMany(p => p.Steps)
            .OrderBy(s => s.Level).ThenBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.Level,
                s.Title,
                s.DelayMinutes,
                s.ScheduleId,
                ScheduleName = s.Schedule!.Name,
                s.TeamId,
                TeamName = s.Team!.Name,
                s.NotifyAllTeamMembers,
                s.NotifyBothOnCall,
                UserIds = s.TargetedUsers.Select(u => u.UserId).ToList(),
            })
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
        {
            return new IncidentEscalationDto
            {
                PolicyId = policyId,
                PolicyName = incident.PolicyName,
                RunState = IncidentEscalationRunState.NotConfigured,
                StartedAt = incident.EscalationStartedAt,
            };
        }

        var stepIds = steps.Select(s => s.Id).ToList();
        // Latest, not earliest: a reopened incident runs the same steps again.
        var pagedAt = await timelineRepo.GetQueryable()
            .AsNoTracking()
            .Where(e => e.IncidentId == incidentId
                        && e.EscalationStepId != null
                        && stepIds.Contains(e.EscalationStepId.Value))
            .GroupBy(e => e.EscalationStepId!.Value)
            .Select(g => new { StepId = g.Key, At = g.Max(e => e.CreatedAt) })
            .ToDictionaryAsync(x => x.StepId, x => x.At, cancellationToken);

        var names = await ResolveTargetNamesAsync(
            steps.SelectMany(s => s.UserIds).Distinct().ToList(), cancellationToken);

        var currentIndex = incident.CurrentEscalationStepId is Guid current
            ? steps.FindIndex(s => s.Id == current)
            : -1;

        var mapped = steps.Select((s, index) => new IncidentEscalationStepDto
        {
            Id = s.Id,
            Level = s.Level,
            Title = s.Title,
            DelayMinutes = s.DelayMinutes,
            ScheduleId = s.ScheduleId,
            ScheduleName = s.ScheduleName,
            TeamId = s.TeamId,
            TeamName = s.TeamName,
            NotifyAllTeamMembers = s.NotifyAllTeamMembers,
            NotifyBothOnCall = s.NotifyBothOnCall,
            NotifyUserNames = s.UserIds
                .Select(id => names.TryGetValue(id, out var name) ? name : null)
                .Where(name => name is not null)
                .Select(name => name!)
                .ToList(),
            // With no pointer the run's position is unknown, but a step the timeline records as
            // having paged is evidence enough that it is behind us.
            State = currentIndex < 0
                ? (pagedAt.ContainsKey(s.Id) ? IncidentEscalationStepState.Passed : IncidentEscalationStepState.Pending)
                : index < currentIndex ? IncidentEscalationStepState.Passed
                : index == currentIndex ? IncidentEscalationStepState.Current
                : IncidentEscalationStepState.Pending,
            PagedAt = pagedAt.TryGetValue(s.Id, out var at) ? at : null,
        }).ToList();

        return new IncidentEscalationDto
        {
            PolicyId = policyId,
            PolicyName = incident.PolicyName,
            // "Never started" is decided by EscalationStartedAt, not by the step pointer: a call
            // acknowledged on the phone clears the pointer on purpose, and reading that as
            // "not started" labels the one incident that certainly did escalate as the one that
            // never did.
            RunState = incident.IsEscalationActive ? IncidentEscalationRunState.Running
                : incident.EscalationStartedAt is null ? IncidentEscalationRunState.Waiting
                : incident.Status is IncidentStatus.Open ? IncidentEscalationRunState.Exhausted
                : IncidentEscalationRunState.Stopped,
            StartedAt = incident.EscalationStartedAt,
            CurrentStepId = incident.CurrentEscalationStepId,
            ExhaustionBehavior = incident.ExhaustionBehavior ?? EscalationExhaustionBehavior.Stop,
            MaxRepeatCycles = incident.MaxRepeatCycles ?? 0,
            CyclesCompleted = incident.EscalationCyclesCompleted,
            NextStepDueAt = NextStepDueAt(
                incident.IsEscalationActive,
                incident.CurrentEscalationStepId is not null,
                currentIndex,
                steps.Select(s => s.DelayMinutes).ToList(),
                incident.EscalationStartedAt ?? incident.CreatedAt,
                incident.LastEscalationStepAt),
            Steps = mapped,
        };
    }

    /// <summary>The instant the step after <paramref name="currentIndex"/> becomes due.</summary>
    // Null whenever the answer would be a guess: nothing is running, the policy is spent, or the
    // step the run was on has since been deleted and its level is no longer readable.
    private static DateTime? NextStepDueAt(
        bool isActive,
        bool hasCurrentStep,
        int currentIndex,
        IReadOnlyList<int> delays,
        DateTime startedAt,
        DateTime? lastStepAt)
    {
        if (!isActive) return null;
        if (hasCurrentStep && currentIndex < 0) return null;

        var next = currentIndex + 1;
        if (next >= delays.Count) return null;

        return hasCurrentStep
            ? EscalationCalculations.NextStepDueAt(lastStepAt ?? startedAt, delays[next])
            : EscalationCalculations.FirstStepDueAt(startedAt, delays[next]);
    }

    private async Task<Dictionary<string, string>> ResolveTargetNamesAsync(
        List<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0) return [];

        var users = await userManager.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => StringExtensions.FormatDisplayName(u.FirstName, u.LastName, u.Email) ?? u.Id);
    }

    /// <summary>The action a status transition deserves, so every path records it the same way.</summary>
    private static AuditAction TransitionAction(IncidentStatus status) => status switch
    {
        IncidentStatus.Acknowledged => AuditAction.Acknowledged,
        IncidentStatus.Resolved => AuditAction.Resolved,
        IncidentStatus.Closed => AuditAction.Closed,
        IncidentStatus.Open => AuditAction.Reopened,
        // Investigating and Mitigated have no action of their own; they are still a status change.
        _ => AuditAction.Updated,
    };

    /// <summary>The fields an auditor asks about, as they stand right now.</summary>
    private static (string Title, string? Description, IncidentSeverity Severity, Guid? ServiceId, Guid? TeamId) Snapshot(Incident incident) =>
        (incident.Title, incident.Description, incident.Severity, incident.ServiceId, incident.TeamId);

    /// <summary>Only what actually moved, so the row is readable rather than a diff of the world.</summary>
    private static List<(string Field, string? Before, string? After)> Diff(
        (string Title, string? Description, IncidentSeverity Severity, Guid? ServiceId, Guid? TeamId) before,
        (string Title, string? Description, IncidentSeverity Severity, Guid? ServiceId, Guid? TeamId) after)
    {
        var changes = new List<(string, string?, string?)>();
        if (before.Title != after.Title) changes.Add(("Title", before.Title, after.Title));
        if (before.Description != after.Description) changes.Add(("Description", before.Description, after.Description));
        if (before.Severity != after.Severity) changes.Add(("Severity", before.Severity.ToString(), after.Severity.ToString()));
        if (before.ServiceId != after.ServiceId) changes.Add(("ServiceId", before.ServiceId?.ToString(), after.ServiceId?.ToString()));
        if (before.TeamId != after.TeamId) changes.Add(("TeamId", before.TeamId?.ToString(), after.TeamId?.ToString()));
        return changes;
    }

    /// <summary>Who an unattended dispatch failure is recorded as.</summary>
    private const string DispatchActor = "system:dispatch";

    /// <summary>Records that an incident was opened with no way to page anyone.</summary>
    // A misconfiguration here is indistinguishable from a quiet night: the incident looks normal and
    // no page is ever sent, so it has to say so where an operator and an auditor both look.
    private async Task RecordNobodyCanBePagedAsync(
        Guid incidentId, string description, string reason, CancellationToken cancellationToken)
    {
        await timelineRepo.AddAsync(new IncidentTimelineEvent
        {
            IncidentId = incidentId,
            EventType = TimelineEventType.NoteAdded,
            Title = "Nobody was paged",
            Description = description,
            ActorUserId = DispatchActor
        }, cancellationToken);

        await auditLogService.LogAsync(
            DispatchActor, AuditAction.EscalationNobodyReached, "Incident", incidentId.ToString(),
            null, reason,
            description: $"Incident opened but nobody could be paged: {reason}",
            cancellationToken: cancellationToken);

        logger.LogWarning(
            "Incident {IncidentId} was created but nobody could be paged: {Reason}", incidentId, reason);
    }

    /// <summary>Who a maintenance window acts as.</summary>
    private const string MaintenanceActor = "system:maintenance";

    /// <summary>The acting user, or the system sentinel when there is no HTTP principal.</summary>
    // An empty string is not "the system": it hashes differently from null while meaning the same
    // thing, and it matches no value an auditor can filter for.
    private string Actor() =>
        string.IsNullOrEmpty(currentUser.UserId) ? SystemCurrentUserService.SystemUserId : currentUser.UserId;

    /// <summary>Ends every Active conference room for the incident.</summary>
    // Idempotent set-based UPDATE in the caller's transaction, so the join URL dies with the incident instead of on the expiry sweep.
    private async Task ExpireActiveConferenceRoomsAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await conferenceRoomRepo.GetQueryable()
            .Where(r => r.IncidentId == incidentId && r.Status == ConferenceRoomStatus.Active)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, ConferenceRoomStatus.Ended)
                      .SetProperty(r => r.EndedAt, (DateTime?)now)
                      .SetProperty(r => r.UpdatedAt, now),
                cancellationToken);
    }
    /// <summary>The language a manually raised incident is assumed to be written in.</summary>
    // The text is typed by an operator, so it is in whatever language the organisation works in, and
    // the voice call reads it with a matching voice. A fixed en-US made a Turkish install speak
    // Turkish titles with an English voice.
    private async Task<string> DefaultDataLanguageAsync(CancellationToken cancellationToken)
    {
        if (organizationSettings is null) return SupportedCultures.Fallback;

        var settings = await organizationSettings.GetSettingsAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(settings?.DefaultCulture)
            ? SupportedCultures.Fallback
            : settings.DefaultCulture;
    }

}
