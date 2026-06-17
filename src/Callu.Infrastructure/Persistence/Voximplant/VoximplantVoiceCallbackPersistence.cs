using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Voximplant;

public class VoximplantVoiceCallbackPersistence(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    IServiceProvider serviceProvider,
    ILogger<VoximplantVoiceCallbackPersistence> logger) : IVoximplantVoiceCallbackPersistence
{
    private const int MaxRetryAttempts = 3;

    public async Task ProcessAsync(
        VoxCallbackRequest callback,
        string? scenarioApiKey,
        VoximplantCallbackProcessingCallbacks callbacks,
        CancellationToken cancellationToken = default)
    {
        VoximplantCallDataServiceLog.VoxEngineCallback(logger,
            callback.IncidentId, callback.Status, callback.Duration);

        var callStatus = MapVoxStatus(callback.Status);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var incidentId = Guid.TryParse(callback.IncidentId, out var incId) ? incId : Guid.Empty;

        if (incidentId == Guid.Empty && !string.IsNullOrEmpty(callback.CallToken))
        {
            var callData = await callbacks.PeekCallTokenAsync(callback.CallToken, cancellationToken);
            if (callData != null)
            {
                incidentId = Guid.TryParse(callData.IncidentId, out var recoveredId) ? recoveredId : Guid.Empty;
                var restoredPhone = callData.Phone ?? "";
                if (string.IsNullOrEmpty(callback.Data?.TryGetValue("phone", out _) == true ? callback.Data["phone"].ToString() : null))
                {
                    callback.Data ??= new Dictionary<string, object>();
                    callback.Data["phone"] = restoredPhone;
                }
            }
        }

        if (incidentId == Guid.Empty && !string.IsNullOrEmpty(callback.ConferenceId))
        {
            var room = await context.ConferenceRooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.VoximplantConferenceId == callback.ConferenceId, cancellationToken);
            if (room != null)
            {
                incidentId = room.IncidentId;
            }
        }

        if (incidentId == Guid.Empty)
        {
            logger.LogWarning("Voximplant callback dropped: Invalid IncidentId and unable to recover via token.");
            return;
        }

        var incident = await context.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);
        if (incident == null)
        {
            logger.LogWarning("Voximplant callback dropped: Incident {IncidentId} not found in DB.", incidentId);
            return;
        }

        if (!string.IsNullOrEmpty(scenarioApiKey) &&
            !await callbacks.ValidateScenarioKeyAsync(scenarioApiKey, cancellationToken))
        {
            logger.LogWarning(
                "Voximplant callback dropped: scenario key rejected for incident {IncidentId}.",
                incidentId);
            return;
        }

        var phoneFromCallback = callback.Data?.TryGetValue("phone", out var phone) == true ? phone?.ToString() ?? "" : "";
        if (phoneFromCallback.Length > 32) phoneFromCallback = phoneFromCallback[..32];

        static string? BuildMetadataJson(Dictionary<string, object>? data)
        {
            const int maxMetadataChars = 4000;
            if (data == null) return null;
            var json = JsonSerializer.Serialize(data);
            return json.Length <= maxMetadataChars
                ? json
                : JsonSerializer.Serialize(new { _truncated = true, originalLength = json.Length });
        }

        var sessionId = string.IsNullOrWhiteSpace(callback.CallSessionId) ? null : callback.CallSessionId.Trim();

        CallLog callLog;
        var isNewSessionCallLog = false;

        if (sessionId != null)
        {
            var existingSessionLog = await context.CallLogs
                .FirstOrDefaultAsync(
                    c => c.CallToken == sessionId,
                    cancellationToken);

            var isTerminal = IsTerminalCallStatus(callStatus);
            var initiatedAt = DateTime.UtcNow.AddSeconds(-Math.Max(0, callback.Duration));

            if (existingSessionLog == null)
            {
                isNewSessionCallLog = true;
                callLog = new CallLog
                {
                    IncidentId = incidentId,
                    PhoneNumber = phoneFromCallback,
                    Status = callStatus,
                    DurationSeconds = callback.Duration,
                    InitiatedAt = initiatedAt,
                    CompletedAt = isTerminal ? DateTime.UtcNow : null,
                    MetadataJson = BuildMetadataJson(callback.Data),
                    CreatedAt = DateTime.UtcNow,
                    CallToken = sessionId,
                    AttemptNumber = 0,
                };
                context.CallLogs.Add(callLog);
            }
            else
            {
                callLog = existingSessionLog;
                if (!string.IsNullOrEmpty(phoneFromCallback))
                    callLog.PhoneNumber = phoneFromCallback;
                callLog.Status = callStatus;
                callLog.DurationSeconds = callback.Duration;
                callLog.MetadataJson = BuildMetadataJson(callback.Data) ?? callLog.MetadataJson;
                callLog.InitiatedAt = initiatedAt;
                callLog.CompletedAt = isTerminal ? DateTime.UtcNow : null;
                callLog.UpdatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            callLog = new CallLog
            {
                IncidentId = incidentId,
                PhoneNumber = phoneFromCallback,
                Status = callStatus,
                DurationSeconds = callback.Duration,
                InitiatedAt = DateTime.UtcNow.AddSeconds(-callback.Duration),
                CompletedAt = DateTime.UtcNow,
                MetadataJson = BuildMetadataJson(callback.Data),
                CreatedAt = DateTime.UtcNow
            };
            context.CallLogs.Add(callLog);
        }

        if (string.IsNullOrEmpty(callLog.PhoneNumber))
        {
            var recentToken = await context.CallTokens
                .AsNoTracking()
                .Where(t => t.CallDataJson.Contains(incidentId.ToString()))
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (recentToken != null)
            {
                try
                {
                    var tokenData = JsonSerializer.Deserialize<VoxCallData>(recentToken.CallDataJson);
                    if (!string.IsNullOrEmpty(tokenData?.Phone))
                        callLog.PhoneNumber = tokenData.Phone;
                }
                catch
                {
                }
            }
        }

        if (!string.IsNullOrEmpty(callLog.PhoneNumber))
        {
            var normalizedPhone = callLog.PhoneNumber.Replace("+", "").Replace(" ", "");
            var user = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    u => u.PhoneNumber != null && u.PhoneNumber.Replace("+", "").Replace(" ", "") == normalizedPhone,
                    cancellationToken);
            callLog.CalledPersonName = user?.DisplayName ?? user?.UserName;
        }

        if (sessionId == null)
        {
            var existingAttempts = await context.CallLogs
                .CountAsync(cl => cl.IncidentId == callLog.IncidentId && cl.PhoneNumber == callLog.PhoneNumber, cancellationToken);
            callLog.AttemptNumber = existingAttempts + 1;
        }
        else if (isNewSessionCallLog)
        {
            if (string.IsNullOrEmpty(callLog.PhoneNumber))
                callLog.AttemptNumber = 1;
            else
            {
                var completedForPhone = await context.CallLogs
                    .CountAsync(
                        cl => cl.IncidentId == incidentId
                            && cl.PhoneNumber == callLog.PhoneNumber
                            && cl.CompletedAt != null,
                        cancellationToken);
                callLog.AttemptNumber = completedForPhone + 1;
            }
        }

        var recipient = !string.IsNullOrEmpty(callLog.CalledPersonName)
            ? $"{callLog.CalledPersonName} ({callLog.PhoneNumber})"
            : callLog.PhoneNumber;

        var timelineEvent = new IncidentTimelineEvent
        {
            IncidentId = callLog.IncidentId,
            ActorName = callLog.CalledPersonName,
            CreatedAt = DateTime.UtcNow
        };

        switch (callStatus)
        {
            case CallStatus.Initiated:
                timelineEvent.EventType = TimelineEventType.CallInitiated;
                timelineEvent.Title = "Call Ringing";
                timelineEvent.Description = $"Outbound call ringing to {recipient}";
                break;
            case CallStatus.Acknowledged:
                if (incident.Status.IsTerminal())
                {
                    logger.LogInformation(
                        "Phone ack ignored for incident {IncidentId}: already {Status}",
                        incident.Id, incident.Status);

                    timelineEvent.EventType = TimelineEventType.CallAcknowledged;
                    timelineEvent.Title = "Phone ack ignored (incident already closed)";
                    timelineEvent.Description =
                        $"Late phone acknowledgement from {recipient} — incident was already {incident.Status}.";
                }
                else
                {
                    incident.Status = IncidentStatus.Acknowledged;
                    incident.AcknowledgedAt = DateTime.UtcNow;
                    incident.AcknowledgedBy = callLog.CalledPersonName ?? "Phone Responder";
                    VoximplantCallDataServiceLog.IncidentAcknowledgedViaCall(logger, callLog.IncidentId);

                    timelineEvent.EventType = TimelineEventType.CallAcknowledged;
                    timelineEvent.Title = "Call Acknowledged";
                    timelineEvent.Description = $"Incident acknowledged via phone call to {recipient}";

                    callLog.NextRetryAt = null;

                    if (incident.IsEscalationActive)
                    {
                        incident.IsEscalationActive = false;
                        incident.CurrentEscalationStepId = null;
                        incident.LastEscalationStepAt = null;
                        logger.LogInformation("Escalation cancelled for incident {IncidentId} due to call acknowledgement", incident.Id);
                    }
                }
                break;

            case CallStatus.Escalated:
                VoximplantCallDataServiceLog.IncidentEscalationRequested(logger, callLog.IncidentId);

                timelineEvent.EventType = TimelineEventType.CallEscalated;
                timelineEvent.Title = "Call Escalated";
                timelineEvent.Description = $"Caller requested escalation during call to {recipient}";

                callLog.NextRetryAt = null;

                if (incident.EscalationPolicyId.HasValue)
                {
                    if (!incident.IsEscalationActive)
                    {
                        incident.EscalationStartedAt = DateTime.UtcNow;
                        incident.IsEscalationActive = true;
                        incident.CurrentEscalationStepId = null;
                        incident.LastEscalationStepAt = null;
                    }
                    else
                    {
                        var steps = await context.Set<EscalationStep>()
                            .Where(s => s.EscalationPolicyId == incident.EscalationPolicyId && !s.IsDeleted)
                            .OrderBy(s => s.Level)
                            .ToListAsync(cancellationToken);

                        var currentIdx = steps.FindIndex(s => s.Id == incident.CurrentEscalationStepId);
                        if (currentIdx >= 0 && currentIdx < steps.Count - 1)
                        {
                            var nextStep = steps[currentIdx + 1];
                            incident.CurrentEscalationStepId = nextStep.Id;
                            incident.LastEscalationStepAt = DateTime.UtcNow;
                            logger.LogInformation(
                                "User-initiated escalation: step {From} → {To} for incident {IncidentId}",
                                currentIdx + 1, currentIdx + 2, incident.Id);
                        }
                        else
                        {
                            incident.IsEscalationActive = false;
                            logger.LogInformation("Escalation completed — no more steps for incident {IncidentId}", incident.Id);
                        }
                    }
                    logger.LogInformation("Escalation triggered for incident {IncidentId} via user call request", incident.Id);
                }
                break;

            case CallStatus.Failed:
            case CallStatus.NoAnswer:
            case CallStatus.Voicemail:
            case CallStatus.Timeout:
                VoximplantCallDataServiceLog.CallEndedWithStatus(logger, callLog.IncidentId, callStatus.ToString(), callLog.AttemptNumber, MaxRetryAttempts);

                timelineEvent.EventType = TimelineEventType.CallFailed;
                timelineEvent.Title = $"Call {callStatus}";
                timelineEvent.Description = $"Call to {recipient} ended ({callStatus}), attempt {callLog.AttemptNumber}/{MaxRetryAttempts}";

                if (callLog.AttemptNumber < MaxRetryAttempts)
                {
                    var backoffSeconds = 30 * Math.Pow(2, Math.Max(0, callLog.AttemptNumber - 1));
                    callLog.NextRetryAt = DateTime.UtcNow.AddSeconds(Math.Min(backoffSeconds, 3600));
                }
                else
                {
                    callLog.NextRetryAt = null;
                    VoximplantCallDataServiceLog.MaxRetryAttemptsReached(logger, callLog.IncidentId, callLog.PhoneNumber);
                }
                break;

            case CallStatus.Connected:
                VoximplantCallDataServiceLog.CallConnected(logger, callLog.IncidentId);
                timelineEvent.EventType = TimelineEventType.CallConnected;
                timelineEvent.Title = "Call Connected";
                timelineEvent.Description = $"Call connected to {recipient}";
                callLog.NextRetryAt = null;
                break;
            case CallStatus.ConferenceCreated:
                timelineEvent.EventType = TimelineEventType.ConferenceCreated;
                timelineEvent.Title = "Video Conference Created";
                timelineEvent.Description = $"Video conference link was generated by the responder on the call with {recipient}";

                callLog.NextRetryAt = null;
                if (!incident.Status.IsTerminal())
                {
                    if (incident.Status != IncidentStatus.Acknowledged)
                    {
                        incident.Status = IncidentStatus.Acknowledged;
                        incident.AcknowledgedAt = DateTime.UtcNow;
                        incident.AcknowledgedBy = callLog.CalledPersonName ?? "Phone Responder";
                        VoximplantCallDataServiceLog.IncidentAcknowledgedViaCall(logger, callLog.IncidentId);
                    }

                    if (incident.IsEscalationActive)
                    {
                        incident.IsEscalationActive = false;
                        incident.CurrentEscalationStepId = null;
                        incident.LastEscalationStepAt = null;
                        logger.LogInformation("Escalation cancelled for incident {IncidentId} due to conference creation", incident.Id);
                    }
                }
                break;

            default:
                timelineEvent.EventType = TimelineEventType.CallInitiated;
                timelineEvent.Title = "Call Initiated";
                timelineEvent.Description = $"Call initiated to {recipient}";
                break;
        }

        context.Set<IncidentTimelineEvent>().Add(timelineEvent);

        await context.SaveChangesAsync(cancellationToken);

        await callbacks.NotifyActiveVoiceCallsChanged(cancellationToken);

        if (callStatus == CallStatus.Escalated && incident.EscalationPolicyId.HasValue && incident.CurrentEscalationStepId.HasValue)
        {
            try
            {
                var currentStep = await context.Set<EscalationStep>()
                    .AsNoTracking()
                    .Include(s => s.TargetedUsers)
                    .FirstOrDefaultAsync(s => s.Id == incident.CurrentEscalationStepId, cancellationToken);

                if (currentStep != null)
                {
                    using var scope = serviceProvider.CreateScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                    var payload = new NotificationPayload
                    {
                        IncidentId = incident.Id,
                        Title = incident.Title,
                        Description = incident.Description,
                        Severity = incident.Severity.ToString(),
                        EventType = NotificationEventType.EscalationStep,
                        EscalationLevel = currentStep.Level
                    };

                    int reached;
                    if (currentStep.ScheduleId.HasValue)
                    {
                        reached = await dispatcher.NotifyOnCallAsync(currentStep.ScheduleId.Value, payload, cancellationToken);
                    }
                    else
                    {
                        var userIds = currentStep.TargetedUsers
                            .Select(u => u.UserId)
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .ToArray();
                        reached = userIds.Length > 0
                            ? await dispatcher.NotifyUsersAsync(userIds, payload, cancellationToken)
                            : 0;
                    }

                    if (reached == 0)
                    {
                        context.Set<IncidentTimelineEvent>().Add(new IncidentTimelineEvent
                        {
                            IncidentId = incident.Id,
                            EventType = TimelineEventType.Escalated,
                            Title = $"Escalation step {currentStep.Level}: nobody paged",
                            Description = "A responder-initiated escalation reached no one (no on-call responder, or no reachable targeted users).",
                            ActorUserId = "system"
                        });
                        await context.SaveChangesAsync(cancellationToken);
                        logger.LogWarning("Immediate escalation for incident {IncidentId} step {Level} reached nobody", incident.Id, currentStep.Level);
                    }
                    else
                    {
                        logger.LogInformation("Immediate dispatch completed for escalation step {Level} on incident {IncidentId}", currentStep.Level, incident.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dispatch immediate escalation notification for incident {IncidentId}", incident.Id);
            }
        }
    }

    internal static CallStatus MapVoxStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "alerting" => CallStatus.Initiated,
            "connected" => CallStatus.Connected,
            "acknowledged" => CallStatus.Acknowledged,
            "escalated" => CallStatus.Escalated,
            "failed" => CallStatus.Failed,
            "no_answer" => CallStatus.NoAnswer,
            "voicemail" => CallStatus.Voicemail,
            "timeout" => CallStatus.Timeout,
            "conference_created" => CallStatus.ConferenceCreated,
            _ => CallStatus.Connected
        };

    internal static bool IsTerminalCallStatus(CallStatus s) =>
        s is CallStatus.Acknowledged
            or CallStatus.Escalated
            or CallStatus.Failed
            or CallStatus.NoAnswer
            or CallStatus.Voicemail
            or CallStatus.Timeout
            or CallStatus.ConferenceCreated;
}
