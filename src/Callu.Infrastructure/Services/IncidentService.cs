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
using Callu.Shared.Results;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Exceptions;
using Callu.Application.Common.Interfaces;
using Callu.Application.Messaging;
using Callu.Infrastructure.Telemetry;

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
    INotificationPushService? pushService = null) : IIncidentService
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
                    string.Empty, "Suppressed", "Service", request.ServiceId.Value.ToString(),
                    null, $"Service in maintenance (SuppressAlerts): {request.Title}", cancellationToken);
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
                    .AnyAsync(i => i.ExternalAlertId == incident.ExternalAlertId
                                && i.Status != IncidentStatus.Resolved
                                && i.Status != IncidentStatus.Closed
                                && !i.IsDeleted, cancellationToken);
                if (exists)
                    throw new ConflictException($"An active incident with ExternalAlertId '{incident.ExternalAlertId}' already exists.");
            }

            incident.Title = incident.Title.Trim();
            incident.Description = incident.Description?.Trim();
            incident.DataLanguage = string.IsNullOrEmpty(request.DataLanguage) ? "en-US" : request.DataLanguage;

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
                string.Empty, "Created", "Incident", incident.Id.ToString(),
                null, System.Text.Json.JsonSerializer.Serialize(request), cancellationToken);

            IncidentServiceLog.IncidentCreated(logger, incident.Id);

            if (autoAcknowledge)
            {
                incident.Acknowledge("system:maintenance");
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
            if (!autoAcknowledge && incident.TeamId.HasValue)
            {
                var policy = await escalationPolicyRepo.FindSingleAsync(
                    p => p.TeamId == incident.TeamId && p.IsActive && !p.IsDeleted, cancellationToken);
                if (policy != null)
                {
                    escalationHandle = await escalationWorkflow.StageForNewIncidentAsync(
                        incident.Id, policy.Id, cancellationToken);
                    stagedPolicyId = policy.Id;
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

        await escalationHandle.DispatchAsync(cancellationToken);
        if (escalationPolicyId.HasValue)
            IncidentServiceLog.EscalationAutoTriggered(logger, escalationIncidentId, escalationPolicyId.Value);

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
                    string.Empty, "DispatchFailed", "NotificationChannel", dto.Id.ToString(),
                    null, $"IncidentCreated: {ex.Message}", cancellationToken);
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

            if (!string.IsNullOrWhiteSpace(request.Title))
                incident.Title = request.Title.Trim();

            if (request.Description != null)
                incident.Description = request.Description.Trim();

            if (!string.IsNullOrWhiteSpace(request.Severity) && Enum.TryParse<IncidentSeverity>(request.Severity, out var severity))
                incident.Severity = severity;

            if (request.ServiceId.HasValue)
                incident.ServiceId = request.ServiceId.Value;

            if (request.TeamId.HasValue)
                incident.TeamId = request.TeamId.Value;

            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<IncidentStatus>(request.Status, ignoreCase: true, out var target))
            {
                var actor = currentUser.UserId ?? "system";
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

                incident.Acknowledge(userId);

                await timelineRepo.AddAsync(new IncidentTimelineEvent
                {
                    IncidentId = incidentId,
                    EventType = TimelineEventType.Acknowledged,
                    Title = "Acknowledged",
                    Description = $"Acknowledged by {userId}",
                    ActorUserId = userId
                }, cancellationToken);

                await callLogRepo.GetQueryable()
                    .Where(c => c.IncidentId == incidentId && c.NextRetryAt != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.NextRetryAt, (DateTime?)null), cancellationToken);

                IncidentServiceLog.EscalationStoppedAck(logger, incidentId);

                await auditLogService.LogAsync(
                    userId, "Updated", "Incident", incidentId.ToString(),
                    "Status: Open", "Status: Acknowledged", cancellationToken);

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

                incident.Resolve(userId);

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
                    userId, "Updated", "Incident", incidentId.ToString(),
                    "Status: " + incident.Status, "Status: Resolved", cancellationToken);

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
                    userId, "Updated", "Incident", incidentId.ToString(),
                    $"Status: {previous}", "Status: Closed", cancellationToken);
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

                if (incident.TeamId.HasValue)
                {
                    var policy = await escalationPolicyRepo.FindSingleAsync(
                        p => p.TeamId == incident.TeamId && p.IsActive && !p.IsDeleted, cancellationToken);
                    if (policy != null)
                    {
                        escalationHandle = await escalationWorkflow.StageForNewIncidentAsync(
                            incident.Id, policy.Id, cancellationToken);
                        stagedPolicyId = policy.Id;
                    }
                }

                await auditLogService.LogAsync(
                    userId, "Updated", "Incident", incidentId.ToString(),
                    $"Status: {previous}", "Status: Open (reopened)", cancellationToken);
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

        await escalationHandle.DispatchAsync(cancellationToken);
        if (stagedPolicyId.HasValue)
            IncidentServiceLog.EscalationAutoTriggered(logger, incidentId, stagedPolicyId.Value);

        await TryDispatchOrgNotificationAsync(incidentId, NotificationChannelDispatchEvent.IncidentReopened, cancellationToken);
        await BroadcastLifecycleAsync(incidentId, "Reopened", cancellationToken);
    }

    /// <summary>
    /// Best-effort SignalR fanout to every connected client so concurrent operators
    /// don't have to wait for TanStack Query's staleTime to see the new state.
    /// Null on the Worker host (no SignalR hub) — silently no-ops. Fix 05.Fix 2
    /// will get the Worker on a Redis-backed backplane so its lifecycle events also push.
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
                throw new InvalidOperationException($"Cannot escalate incident in '{incident.Status}' status.");

            var escalated = false;
            if (incident.IsEscalationActive)
            {
                escalated = await escalationOrchestrator.AdvanceEscalationAsync(incident.Id, cancellationToken);
            }
            else if (incident.TeamId.HasValue)
            {
                var policy = await escalationPolicyRepo.FindSingleAsync(
                    p => p.TeamId == incident.TeamId && p.IsActive && !p.IsDeleted, cancellationToken);

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
                await auditLogService.LogAsync(userId, "Escalated", "Incident", incidentId.ToString(), null, reason, cancellationToken);

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
                throw new InvalidOperationException($"Cannot reassign a {incident.Status} incident. Reopen first.");

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

            await auditLogService.LogAsync(assignedBy, "Reassigned", "Incident", incidentId.ToString(), previousAssignee, targetUserId, cancellationToken);

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

    /// <summary>
    /// Fetch a tracked incident for mutation with team-scoping applied.
    /// Throws <see cref="NotFoundException"/> if the incident does not exist OR the
    /// caller does not have visibility to it — we deliberately do not distinguish
    /// between the two to avoid leaking existence of out-of-scope incidents.
    /// </summary>
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
        
        if (!string.IsNullOrEmpty(filter.Severity) && Enum.TryParse<IncidentSeverity>(filter.Severity, out var severity))
            query = query.Where(i => i.Severity == severity);
        
        if (filter.ServiceId.HasValue)
            query = query.Where(i => i.ServiceId == filter.ServiceId.Value);
        
        if (filter.TeamId.HasValue)
            query = query.Where(i => i.TeamId == filter.TeamId.Value);
        
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

        if (currentUser.IsInRole("Admin") || currentUser.IsInRole("Owner"))
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
            .Select(i => new { i.Title, i.Severity, i.ServiceId })
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot == null) return;

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
                    string.Empty, "DispatchFailed", "NotificationChannel", incidentId.ToString(),
                    null, $"{dispatchEvent}: {ex.Message}", cancellationToken);
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

    /// <summary>
    /// Flip every Active conference room for the incident to Ended + set EndedAt
    /// = now. Idempotent — runs as a single set-based UPDATE inside the caller's
    /// transaction, no-op when no Active rows exist. Called from Resolve and
    /// Close paths so the join URL stops working as soon as the incident is
    /// closed instead of waiting up to 60 minutes for the Quartz expiry sweep.
    /// </summary>
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
}
