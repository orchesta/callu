using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Models.Communication;
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

    public async Task<VoxCallbackResult> ProcessAsync(
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

        // A DROPPED callback paged nobody, and if it was a press-2 the responder must hear that rather
        // than "escalation has been initiated". Every early return below therefore reports what the
        // keypress achieved — which for a dropped callback is nothing at all.
        var requestedEscalation = callStatus == CallStatus.Escalated;
        var pagedNobody = new VoxCallbackResult(requestedEscalation, false);

        if (incidentId == Guid.Empty)
        {
            logger.LogWarning("Voximplant callback dropped: Invalid IncidentId and unable to recover via token.");
            return pagedNobody;
        }

        var incident = await context.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);
        if (incident == null)
        {
            logger.LogWarning("Voximplant callback dropped: Incident {IncidentId} not found in DB.", incidentId);
            return pagedNobody;
        }

        if (!string.IsNullOrEmpty(scenarioApiKey) &&
            !await callbacks.ValidateScenarioKeyAsync(scenarioApiKey, cancellationToken))
        {
            logger.LogWarning(
                "Voximplant callback dropped: scenario key rejected for incident {IncidentId}.",
                incidentId);
            return pagedNobody;
        }

        // Conference-room lifecycle callbacks are not phone calls, so they must never write a CallLog.
        // "conference_created" is different: it comes from the incident call and acknowledges below.
        if (IsConferenceLifecycleStatus(callback.Status))
        {
            logger.LogDebug(
                "Conference lifecycle callback '{Status}' for incident {IncidentId} — no call log written.",
                callback.Status, incidentId);
            return VoxCallbackResult.None;
        }

        var phoneFromCallback = callback.Data?.TryGetValue("phone", out var phone) == true ? phone?.ToString() ?? "" : "";
        if (phoneFromCallback.Length > 32) phoneFromCallback = phoneFromCallback[..32];

        // Absent on a call placed by a script older than contract 1.6, and on the conference scenario.
        static Guid? AttemptIdOrNull(string? attemptId) =>
            Guid.TryParse(attemptId, out var parsed) ? parsed : null;

        string? ReportedValue(string key) =>
            callback.Data?.TryGetValue(key, out var value) == true ? value?.ToString() : null;

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

            // The scenario retries a callback whose ack it never saw. The row already carrying this
            // terminal status means it was fully applied, so ack and stop rather than replay it.
            if (existingSessionLog != null && isTerminal && existingSessionLog.Status == callStatus)
            {
                logger.LogInformation(
                    "Voximplant callback '{Status}' for incident {IncidentId} already applied to call session {Session} — treating as a retry.",
                    callback.Status, incidentId, sessionId);

                if (requestedEscalation)
                    // The earlier delivery ran the escalation, but whether it paged anybody is not
                    // knowable here — so tell the responder to escalate rather than reassure them.
                    logger.LogWarning(
                        "Responder-initiated escalation for incident {IncidentId} was already applied on an earlier delivery of "
                        + "this callback; whether it paged anybody cannot be confirmed from here. The responder is being told to "
                        + "escalate from Callu. The earlier delivery's outcome is on the incident timeline.",
                        incidentId);

                return pagedNobody;
            }

            // A non-terminal callback landing after its session already ended is out of order and
            // carries no news; applying it would overwrite the outcome and disarm the retry chain.
            if (existingSessionLog != null && !isTerminal && existingSessionLog.CompletedAt != null)
            {
                logger.LogInformation(
                    "Voximplant callback '{Status}' for incident {IncidentId} arrived after call session {Session} had already ended — ignored.",
                    callback.Status, incidentId, sessionId);
                return VoxCallbackResult.None;
            }

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
                    AttemptId = AttemptIdOrNull(callback.AttemptId),
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
                // CompletedAt is only ever set, never cleared: it is the anchor the voice-retry sweep
                // uses to decide whether a newer call has started for this responder.
                if (isTerminal) callLog.CompletedAt = DateTime.UtcNow;
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

        // Who the keypress is attributed to. A display name is enough for the timeline a responder
        // reads, but not for the audit trail: it is editable and not unique, so the id is what the
        // trail records and the incident stores.
        string? actorUserId = null;

        if (!string.IsNullOrEmpty(callLog.PhoneNumber))
        {
            var normalizedPhone = callLog.PhoneNumber.Replace("+", "").Replace(" ", "");
            // Two rows are read, not one: a phone number is not unique, and attributing an
            // acknowledgement to whichever of two people the database happened to return is worse
            // than admitting the match was ambiguous.
            var matches = await context.Users
                .AsNoTracking()
                .Where(u => u.PhoneNumber != null && u.PhoneNumber.Replace("+", "").Replace(" ", "") == normalizedPhone)
                .OrderBy(u => u.Id)
                .Take(2)
                .Select(u => new { u.Id, u.DisplayName, u.UserName })
                .ToListAsync(cancellationToken);

            if (matches.Count == 1)
            {
                actorUserId = matches[0].Id;
                callLog.CalledPersonName = matches[0].DisplayName ?? matches[0].UserName;
            }
            else if (matches.Count > 1)
            {
                logger.LogWarning(
                    "Phone {Phone} matches more than one user; the keypress is recorded without an actor id",
                    callLog.PhoneNumber);
            }
        }

        // Written after the commit below, so a slow or failing audit sink cannot hold up the answer
        // the VoxEngine scenario is waiting on.
        var auditRows = new List<(AuditAction Action, string? Old, string? New, string Description)>();

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

        var escalationRequested = false;

        // Once the incident has a human on it, every armed voice retry for it stands down — tracked, not
        // ExecuteUpdate, so it commits in the same SaveChanges as the acknowledgement.
        async Task DisarmIncidentVoiceRetriesAsync()
        {
            var armed = await context.CallLogs
                .Where(c => c.IncidentId == incidentId && c.NextRetryAt != null)
                .ToListAsync(cancellationToken);

            foreach (var row in armed)
            {
                row.StandDownRetryChain();
                row.UpdatedAt = DateTime.UtcNow;
            }
        }

        switch (callStatus)
        {
            case CallStatus.Initiated:
                timelineEvent.EventType = TimelineEventType.CallInitiated;
                timelineEvent.Title = "Call Ringing";
                timelineEvent.Description = $"Outbound call ringing to {recipient}";
                break;
            case CallStatus.Acknowledged:
                timelineEvent.EventType = TimelineEventType.CallAcknowledged;

                if (incident.Status.IsTerminal())
                {
                    logger.LogInformation(
                        "Phone ack ignored for incident {IncidentId}: already {Status}",
                        incident.Id, incident.Status);

                    timelineEvent.Title = "Phone ack ignored (incident already closed)";
                    timelineEvent.Description =
                        $"Late phone acknowledgement from {recipient} — incident was already {incident.Status}.";
                }
                else if (incident.Status == IncidentStatus.Open)
                {
                    var wasEscalating = incident.IsEscalationActive;
                    incident.Acknowledge(actorUserId ?? callLog.CalledPersonName ?? "Phone Responder");
                    VoximplantCallDataServiceLog.IncidentAcknowledgedViaCall(logger, callLog.IncidentId);
                    auditRows.Add((AuditAction.Acknowledged, "Status: Open", "Status: Acknowledged",
                        $"Acknowledged by keypress on the call to {recipient}"));

                    timelineEvent.Title = "Call Acknowledged";
                    timelineEvent.Description = $"Incident acknowledged via phone call to {recipient}";

                    callLog.StandDownRetryChain();
                    await DisarmIncidentVoiceRetriesAsync();

                    if (wasEscalating)
                    {
                        incident.CurrentEscalationStepId = null;
                        incident.LastEscalationStepAt = null;
                        logger.LogInformation("Escalation cancelled for incident {IncidentId} due to call acknowledgement", incident.Id);
                    }
                }
                else
                {
                    logger.LogInformation(
                        "Phone ack for incident {IncidentId} did not change status: already {Status}",
                        incident.Id, incident.Status);

                    timelineEvent.Title = "Phone ack received";
                    timelineEvent.Description =
                        $"Phone acknowledgement from {recipient} — incident already in progress ({incident.Status}).";
                    auditRows.Add((AuditAction.Acknowledged, $"Status: {incident.Status}", $"Status: {incident.Status}",
                        $"Keypress on the call to {recipient} stood down the retries; the incident was already {incident.Status}"));

                    callLog.StandDownRetryChain();
                    await DisarmIncidentVoiceRetriesAsync();

                    if (incident.IsEscalationActive)
                    {
                        incident.IsEscalationActive = false;
                        incident.CurrentEscalationStepId = null;
                        incident.LastEscalationStepAt = null;
                        incident.UpdatedAt = DateTime.UtcNow;
                        logger.LogInformation("Escalation cancelled for incident {IncidentId} due to call acknowledgement", incident.Id);
                    }
                }
                break;

            case CallStatus.Escalated:
                VoximplantCallDataServiceLog.IncidentEscalationRequested(logger, callLog.IncidentId);

                timelineEvent.EventType = TimelineEventType.CallEscalated;
                timelineEvent.Title = "Call Escalated";
                timelineEvent.Description = $"Caller requested escalation during call to {recipient}";
                auditRows.Add((AuditAction.Escalated, null, null,
                    $"Escalation requested by keypress on the call to {recipient}"));

                callLog.StandDownRetryChain();

                // This branch deliberately owns no escalation state: everything about escalating belongs
                // to EscalationOrchestrator and runs below, once the keypress is durably recorded.
                escalationRequested = true;
                break;

            case CallStatus.Failed:
            case CallStatus.NoAnswer:
            case CallStatus.Voicemail:
            case CallStatus.Timeout:
                VoximplantCallDataServiceLog.CallEndedWithStatus(logger, callLog.IncidentId, callStatus.ToString(), callLog.AttemptNumber, MaxRetryAttempts);

                callLog.FailureReason = VoiceCallFailureReason.Describe(callStatus, ReportedValue);

                timelineEvent.EventType = TimelineEventType.CallFailed;
                timelineEvent.Title = $"Call {callStatus}";
                timelineEvent.Description = $"Call to {recipient} ended ({callStatus}), attempt {callLog.AttemptNumber}/{MaxRetryAttempts}"
                    + VoiceCallFailureReason.Clause(callLog.FailureReason);

                if (callLog.AttemptNumber < MaxRetryAttempts)
                {
                    var backoffSeconds = 30 * Math.Pow(2, Math.Max(0, callLog.AttemptNumber - 1));
                    callLog.NextRetryAt = DateTime.UtcNow.AddSeconds(Math.Min(backoffSeconds, 3600));
                }
                else
                {
                    callLog.StandDownRetryChain();
                    VoximplantCallDataServiceLog.MaxRetryAttemptsReached(logger, callLog.IncidentId, callLog.PhoneNumber);
                }
                break;

            case CallStatus.Connected:
                VoximplantCallDataServiceLog.CallConnected(logger, callLog.IncidentId);
                timelineEvent.EventType = TimelineEventType.CallConnected;
                timelineEvent.Title = "Call Connected";
                timelineEvent.Description = $"Call connected to {recipient}";
                callLog.StandDownRetryChain();
                break;
            case CallStatus.ConferenceCreated:
                timelineEvent.EventType = TimelineEventType.ConferenceCreated;
                timelineEvent.Title = "Video Conference Created";
                timelineEvent.Description = $"Video conference link was generated by the responder on the call with {recipient}";

                callLog.StandDownRetryChain();
                if (!incident.Status.IsTerminal())
                {
                    var wasEscalatingOnConference = incident.IsEscalationActive;

                    if (incident.Status == IncidentStatus.Open)
                    {
                        incident.Acknowledge(actorUserId ?? callLog.CalledPersonName ?? "Phone Responder");
                        VoximplantCallDataServiceLog.IncidentAcknowledgedViaCall(logger, callLog.IncidentId);
                        auditRows.Add((AuditAction.Acknowledged, "Status: Open", "Status: Acknowledged",
                            $"Acknowledged implicitly by starting a conference on the call to {recipient}"));
                    }

                    await DisarmIncidentVoiceRetriesAsync();

                    if (wasEscalatingOnConference)
                    {
                        incident.IsEscalationActive = false;
                        incident.CurrentEscalationStepId = null;
                        incident.LastEscalationStepAt = null;
                        incident.UpdatedAt = DateTime.UtcNow;
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

        await WriteAuditAsync(incidentId, actorUserId, auditRows, cancellationToken);

        await callbacks.NotifyActiveVoiceCallsChanged(cancellationToken);

        if (!escalationRequested) return VoxCallbackResult.None;

        // The one answer that is spoken out loud: the scenario waits on this to choose between
        // "escalation has been initiated" and "the incident is still open — escalate it in Callu".
        var pagedSomeone = await EscalateOnBehalfOfCallerAsync(incidentId, cancellationToken);
        return new VoxCallbackResult(EscalationRequested: true, EscalationPagedSomeone: pagedSomeone);
    }

    /// <summary>Hands a responder's press-2 to <see cref="IEscalationOrchestrator.EscalateNowAsync"/>,
    /// after the keypress itself is committed so a replayed callback is a no-op.</summary>
    /// <returns>Whether somebody was actually paged; every path that pages nobody must return false.</returns>
    /// <summary>Writes the audit rows a keypress earned, after the keypress itself is committed.</summary>
    // A responder taking an incident from the phone stops every page going out, and until now it
    // left nothing in the tamper-evident trail. It is written outside the commit and swallowed on
    // failure: the scenario is waiting on this request, and a dropped audit row is a smaller loss
    // than a responder hearing the wrong prompt.
    private async Task WriteAuditAsync(
        Guid incidentId,
        string? actorUserId,
        IReadOnlyList<(AuditAction Action, string? Old, string? New, string Description)> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            foreach (var row in rows)
            {
                await audit.LogAsync(
                    actorUserId, row.Action, "Incident", incidentId.ToString(),
                    row.Old, row.New, description: row.Description, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not write the audit trail for a phone keypress on incident {IncidentId}; the keypress itself was applied",
                incidentId);
        }
    }

    private async Task<bool> EscalateOnBehalfOfCallerAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IEscalationOrchestrator>();

            var pagedSomeone = await orchestrator.EscalateNowAsync(incidentId, cancellationToken);

            if (pagedSomeone)
            {
                logger.LogInformation(
                    "Responder-initiated escalation paged the next step of incident {IncidentId}.", incidentId);
                return true;
            }

            logger.LogWarning(
                "Responder-initiated escalation for incident {IncidentId} PAGED NOBODY (the incident is resolved/closed, "
                + "its policy has no step left to hand on to, the escalation could not be resumed, the step's rota was empty, "
                + "or the update lost a race). The reason is on the incident timeline. The responder is being told, on the "
                + "call, that the incident is still open and they must escalate it from Callu — unless someone is paged by "
                + "hand, nobody else is coming.",
                incidentId);
            return false;
        }
        catch (Exception ex)
        {
            // Careful with what this claims. It used to say the sweep still owned the incident's next
            // step — which is false for exactly the cases that need the operator's attention: an
            // exhausted policy has no next step, and an acknowledged incident is not swept at all.
            logger.LogError(ex,
                "Responder-initiated escalation for incident {IncidentId} could not be advanced, so the keypress paged nobody. "
                + "The keypress itself is recorded and escalation state is untouched. The sweep carries this incident on only "
                + "if it is still open with an active escalation that has a step left; otherwise nobody else will be paged.",
                incidentId);
            return false;
        }
    }

    /// <summary>Conference-room lifecycle events, which are not phone calls and must never produce
    /// CallLog rows; "conference_created" is excluded on purpose.</summary>
    internal static bool IsConferenceLifecycleStatus(string? status) =>
        status?.ToLowerInvariant()
            is "conference_started" or "conference_ended" or "participant_joined" or "participant_left";

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
