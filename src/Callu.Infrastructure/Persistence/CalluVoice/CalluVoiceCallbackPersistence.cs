using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Conference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Callu.Shared.Logging;

namespace Callu.Infrastructure.Persistence.CalluVoice;

public sealed class CalluVoiceCallbackPersistence(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    IServiceProvider serviceProvider,
    ILogger<CalluVoiceCallbackPersistence> logger) : ICalluVoiceCallbackPersistence
{
    /// <summary>Kept in step with the Voximplant callback and the retry sweep — every voice path stops at the same attempt.</summary>
    private const int MaxRetryAttempts = 3;

    private const int BackoffBaseSeconds = 30;
    private const int BackoffCapSeconds = 3600;
    private const int MaxMetadataChars = 4000;

    public async Task<CalluVoiceCallbackApplication> ProcessAsync(
        CalluVoiceCallbackTicket ticket,
        CalluVoiceCallbackRequest callback,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ApplyAsync(ticket, callback, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // The row is written before anything is dispatched, so the losing delivery has done nothing yet.
            logger.LogInformation(
                "callu-voice callback '{Status}' for incident {IncidentId} lost the insert race for call {CallId} — "
                + "re-applying it on top of the row the other delivery wrote.",
                LogSafe.OneLine(callback.Status), ticket.IncidentId, ticket.CallId);

            return await ApplyAsync(ticket, callback, cancellationToken);
        }
    }

    private async Task<CalluVoiceCallbackApplication> ApplyAsync(
        CalluVoiceCallbackTicket ticket,
        CalluVoiceCallbackRequest callback,
        CancellationToken cancellationToken)
    {
        var outcome = CalluVoiceCallStatus.Map(callback.Status);

        if (!CalluVoiceCallStatus.IsKnown(callback.Status))
            logger.LogWarning(
                "callu-voice reported status '{Status}' for incident {IncidentId}, which this version does not know. "
                + "It is recorded as a failed call, so the retry chain keeps paging rather than going quiet.",
                LogSafe.OneLine(callback.Status), ticket.IncidentId);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var incident = await context.Incidents
            .FirstOrDefaultAsync(i => i.Id == ticket.IncidentId, cancellationToken);

        if (incident is null)
        {
            logger.LogError(
                "callu-voice callback '{Status}' dropped: incident {IncidentId} no longer exists, so the keypress on "
                + "the call to {Phone} is recorded nowhere.",
                LogSafe.OneLine(callback.Status), ticket.IncidentId, PiiRedactor.Phone(ticket.PhoneNumber));
            return CalluVoiceCallbackApplication.IncidentNotFound;
        }

        var existing = await context.CallLogs
            .FirstOrDefaultAsync(c => c.CallToken == ticket.CallId, cancellationToken);

        // callu-voice retries the same body and signs nothing, so a repeat is indistinguishable from a
        // fresh event except by what is already stored for this call.
        if (existing is not null && existing.Status == outcome.Status)
        {
            logger.LogInformation(
                "callu-voice callback '{Status}' for incident {IncidentId} was already applied to call {CallId} — treating it as a retry.",
                LogSafe.OneLine(callback.Status), ticket.IncidentId, ticket.CallId);
            return CalluVoiceCallbackApplication.AlreadyApplied;
        }

        // A settled call takes no further callbacks. Applying one would overwrite the outcome and
        // disarm the chain — or turn an acknowledgement back into an unanswered call.
        if (existing is not null && existing.CompletedAt is not null)
        {
            logger.LogInformation(
                "callu-voice callback '{Status}' for incident {IncidentId} arrived after call {CallId} had already ended as {Ended} — ignored.",
                LogSafe.OneLine(callback.Status), ticket.IncidentId, ticket.CallId, existing.Status);
            return CalluVoiceCallbackApplication.OutOfOrder;
        }

        var now = DateTime.UtcNow;
        var duration = DurationSeconds(callback.DurationSeconds);
        var phone = Clamp(ticket.PhoneNumber, 30);

        var callLog = existing;
        if (callLog is null)
        {
            callLog = new CallLog
            {
                IncidentId = ticket.IncidentId,
                PhoneNumber = phone,
                CallToken = Clamp(ticket.CallId, 100),
                AttemptId = Guid.TryParse(ticket.CallId, out var attemptId) ? attemptId : null,
                InitiatedAt = now.AddSeconds(-duration),
                CreatedAt = now,
                AttemptNumber = await NextAttemptNumberAsync(context, ticket.IncidentId, phone, cancellationToken)
            };
            context.CallLogs.Add(callLog);
        }
        else
        {
            callLog.UpdatedAt = now;
        }

        callLog.Status = outcome.Status;
        callLog.DurationSeconds = duration;
        callLog.MetadataJson = Metadata(callback.Data) ?? callLog.MetadataJson;

        if (VoiceCallFailureReason.Describe(outcome.Status, key => callback.Data?.GetValueOrDefault(key)) is { } why)
            callLog.FailureReason = why;

        // Only ever set, never cleared: the retry sweep uses it to tell whether a newer call has started.
        if (outcome.EndsTheCall) callLog.CompletedAt = now;

        var actor = await ResolveActorAsync(context, phone, cancellationToken);
        if (actor.DisplayName is not null) callLog.CalledPersonName = Clamp(actor.DisplayName, 100);

        var recipient = string.IsNullOrEmpty(callLog.CalledPersonName)
            ? callLog.PhoneNumber
            : $"{callLog.CalledPersonName} ({callLog.PhoneNumber})";

        var audit = new List<(AuditAction Action, string? Old, string? New, string Description)>();
        var isKeypress = IsKeypress(outcome.Status);
        var incidentWasTerminal = incident.Status.IsTerminal();

        var timeline = new IncidentTimelineEvent
        {
            IncidentId = ticket.IncidentId,
            ActorUserId = isKeypress ? actor.UserId : null,
            ActorName = callLog.CalledPersonName,
            CreatedAt = now,
            EventType = TimelineEventTypeFor(outcome.Status),
            Title = TitleFor(outcome.Status, callback.Status),
            Description = DescriptionFor(outcome.Status, callback.Status, recipient, callLog.AttemptNumber, callLog.FailureReason)
        };

        if (outcome.StandsDownRetryChain)
            callLog.StandDownRetryChain();
        else if (outcome.EndsTheCall)
            ArmOrStopTheChain(callLog, incident, now);

        if (outcome.AcknowledgesTheIncident)
        {
            await TakeTheIncidentAsync(context, incident, actor, recipient, outcome.Status, audit, timeline, cancellationToken);
        }
        else if (outcome.Status == CallStatus.Escalated)
        {
            audit.Add((AuditAction.Escalated, null, null,
                $"Escalation requested by keypress on the call to {recipient}"
                + (incidentWasTerminal ? $"; the incident was already {incident.Status}" : string.Empty)));

            if (incidentWasTerminal)
            {
                timeline.Title = "Phone keypress ignored (incident already closed)";
                timeline.Description =
                    $"Late escalation keypress from {recipient} — the incident was already {incident.Status}, so nothing was paged.";
            }
        }

        context.Set<IncidentTimelineEvent>().Add(timeline);
        await context.SaveChangesAsync(cancellationToken);

        await WriteAuditAsync(ticket.IncidentId, actor.UserId, audit, cancellationToken);

        if (incidentWasTerminal) return CalluVoiceCallbackApplication.Applied;

        if (outcome.Status == CallStatus.Escalated)
            await HandOnToTheNextStepAsync(ticket.IncidentId, actor, recipient, cancellationToken);
        else if (outcome.Status == CallStatus.ConferenceRequested)
            await BringTheResponderInAsync(ticket.IncidentId, actor, recipient, cancellationToken);

        return CalluVoiceCallbackApplication.Applied;
    }

    // ----------------------------------------------------------------------------- the call log

    private static int DurationSeconds(double reported) =>
        double.IsFinite(reported) ? (int)Math.Clamp(reported, 0, int.MaxValue) : 0;

    private static string Clamp(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string? Metadata(Dictionary<string, string>? data)
    {
        if (data is null || data.Count == 0) return null;

        var json = JsonSerializer.Serialize(data);
        return json.Length <= MaxMetadataChars
            ? json
            : JsonSerializer.Serialize(new { _truncated = true, originalLength = json.Length });
    }

    private static async Task<int> NextAttemptNumberAsync(
        ApplicationDbContext context, Guid incidentId, string phone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(phone)) return 1;

        var completed = await context.CallLogs.CountAsync(
            c => c.IncidentId == incidentId && c.PhoneNumber == phone && c.CompletedAt != null,
            cancellationToken);

        return completed + 1;
    }

    /// <summary>Arms the next attempt for a call that ended without anyone taking the page.</summary>
    private void ArmOrStopTheChain(CallLog callLog, Incident incident, DateTime now)
    {
        if (incident.Status != IncidentStatus.Open)
        {
            callLog.StandDownRetryChain();
            return;
        }

        if (callLog.AttemptNumber >= MaxRetryAttempts)
        {
            callLog.StandDownRetryChain();
            logger.LogWarning(
                "callu-voice: attempt {Attempt} of {Max} to {Phone} for incident {IncidentId} ended without an answer; "
                + "the retry chain stops here and nobody else is called on this responder's behalf.",
                callLog.AttemptNumber, MaxRetryAttempts, PiiRedactor.Phone(callLog.PhoneNumber), callLog.IncidentId);
            return;
        }

        var backoff = BackoffBaseSeconds * Math.Pow(2, Math.Max(0, callLog.AttemptNumber - 1));
        callLog.NextRetryAt = now.AddSeconds(Math.Min(backoff, BackoffCapSeconds));
        callLog.DialOutFailingSince = null;
        callLog.DialOutFailureKind = null;
    }

    // ----------------------------------------------------------------------------- attribution

    /// <summary>Who the keypress belongs to, or nobody when the number cannot name one person.</summary>
    private readonly record struct Actor(string? UserId, string? DisplayName);

    // A phone number is not unique. Attributing an acknowledgement to whichever of two people the
    // database happened to return is worse than admitting the match was ambiguous.
    private async Task<Actor> ResolveActorAsync(
        ApplicationDbContext context, string phone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(phone)) return default;

        var normalized = phone.Replace("+", "").Replace(" ", "");

        var matches = await context.Users
            .AsNoTracking()
            .Where(u => u.PhoneNumber != null && u.PhoneNumber.Replace("+", "").Replace(" ", "") == normalized)
            .OrderBy(u => u.Id)
            .Take(2)
            .Select(u => new { u.Id, u.DisplayName, u.UserName })
            .ToListAsync(cancellationToken);

        if (matches.Count == 1)
            return new Actor(matches[0].Id, matches[0].DisplayName ?? matches[0].UserName);

        if (matches.Count > 1)
            logger.LogWarning(
                "Phone {Phone} matches more than one user; the keypress is recorded without an actor id",
                PiiRedactor.Phone(phone));

        return default;
    }

    // ----------------------------------------------------------------------------- the incident

    private async Task TakeTheIncidentAsync(
        ApplicationDbContext context,
        Incident incident,
        Actor actor,
        string recipient,
        CallStatus status,
        List<(AuditAction Action, string? Old, string? New, string Description)> audit,
        IncidentTimelineEvent timeline,
        CancellationToken cancellationToken)
    {
        var how = status == CallStatus.ConferenceRequested
            ? $"Keypress asking to be brought in on the call to {recipient}"
            : $"Acknowledged by keypress on the call to {recipient}";

        if (incident.Status.IsTerminal())
        {
            audit.Add((AuditAction.Acknowledged, $"Status: {incident.Status}", $"Status: {incident.Status}",
                $"{how}; the incident was already {incident.Status}"));

            timeline.Title = "Phone keypress ignored (incident already closed)";
            timeline.Description =
                $"Late keypress from {recipient} — the incident was already {incident.Status}, so nothing changed.";
            return;
        }

        var wasEscalating = incident.IsEscalationActive;

        if (incident.Status == IncidentStatus.Open)
        {
            incident.Acknowledge(actor.UserId ?? actor.DisplayName ?? "Phone Responder");
            audit.Add((AuditAction.Acknowledged, "Status: Open", "Status: Acknowledged", how));

            // A keypress that owns the incident as a side effect still has to say so on its own line:
            // this event's own title is about what the key asked for, not about the ownership change.
            if (status != CallStatus.Acknowledged)
            {
                context.Add(new IncidentTimelineEvent
                {
                    IncidentId = incident.Id,
                    ActorUserId = actor.UserId,
                    ActorName = actor.DisplayName,
                    CreatedAt = DateTime.UtcNow,
                    EventType = TimelineEventType.CallAcknowledged,
                    Title = "Incident acknowledged on the call",
                    Description = $"{how}, which also took the incident."
                });
            }
        }
        else
        {
            audit.Add((AuditAction.Acknowledged, $"Status: {incident.Status}", $"Status: {incident.Status}",
                $"{how} stood down the retries; the incident was already {incident.Status}"));
        }

        await DisarmEveryArmedRetryAsync(context, incident.Id, cancellationToken);

        if (wasEscalating)
        {
            incident.IsEscalationActive = false;
            incident.CurrentEscalationStepId = null;
            incident.LastEscalationStepAt = null;
            incident.UpdatedAt = DateTime.UtcNow;
            logger.LogInformation(
                "Escalation cancelled for incident {IncidentId}: a responder took it on the phone", incident.Id);
        }
    }

    // Tracked rather than ExecuteUpdate, so the stand-down commits with the acknowledgement itself.
    private static async Task DisarmEveryArmedRetryAsync(
        ApplicationDbContext context, Guid incidentId, CancellationToken cancellationToken)
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

    // ----------------------------------------------------------------------------- what to say

    /// <summary>Whether the status is one a human produced by pressing a key.</summary>
    private static bool IsKeypress(CallStatus status) =>
        status is CallStatus.Acknowledged or CallStatus.Escalated or CallStatus.ConferenceRequested;

    private static TimelineEventType TimelineEventTypeFor(CallStatus status) => status switch
    {
        CallStatus.Initiated => TimelineEventType.CallInitiated,
        CallStatus.Connected => TimelineEventType.CallConnected,
        CallStatus.Acknowledged => TimelineEventType.CallAcknowledged,
        CallStatus.Escalated => TimelineEventType.CallEscalated,
        CallStatus.ConferenceRequested => TimelineEventType.ConferenceCreated,
        _ => TimelineEventType.CallFailed
    };

    private static string TitleFor(CallStatus status, string? reported) => status switch
    {
        CallStatus.Initiated => "Call ringing",
        CallStatus.Connected => "Call connected",
        CallStatus.Acknowledged => "Call acknowledged",
        CallStatus.Escalated => "Escalation requested on the call",
        CallStatus.ConferenceRequested => "Responder asked to be brought in",
        CallStatus.Voicemail => "Call reached voicemail",
        CallStatus.NoAnswer => "Call not answered",
        CallStatus.SilenceTimeout => "Call answered, no key pressed",
        CallStatus.Timeout => "Call timed out",
        _ => CalluVoiceCallStatus.IsKnown(reported) ? "Call failed" : "Call reported an unknown status"
    };

    private static string DescriptionFor(
        CallStatus status, string? reported, string recipient, int attempt, string? failureReason) =>
        status switch
        {
            CallStatus.Initiated => $"Outbound call ringing to {recipient}",
            CallStatus.Connected => $"Call connected to {recipient}",
            CallStatus.Acknowledged => $"Incident acknowledged by keypress on the call to {recipient}",
            CallStatus.Escalated => $"{recipient} asked for the incident to be escalated during the call",
            CallStatus.ConferenceRequested =>
                $"{recipient} asked to be brought in. The call has already ended, so they were not bridged onto "
                + "anything — the join link is sent to the incident's team by SMS and email.",
            _ when !CalluVoiceCallStatus.IsKnown(reported) =>
                $"The voice service reported status '{reported}' for the call to {recipient}, which this version of "
                + $"Callu does not recognise. It is recorded as a failed call (attempt {attempt} of {MaxRetryAttempts}) "
                + "so the retry chain keeps paging."
                + VoiceCallFailureReason.Clause(failureReason),
            _ => $"Call to {recipient} ended ({status}), attempt {attempt} of {MaxRetryAttempts}"
                 + VoiceCallFailureReason.Clause(failureReason)
        };

    // ----------------------------------------------------------------------------- after the commit

    // Written outside the commit and swallowed on failure: a failing audit sink must not drop a live
    // call's callback. The timeline row for the same keypress is inside the commit, so an operator
    // still sees it happened.
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

    private async Task WriteNobodyInvitedAuditAsync(
        Guid incidentId, Actor actor, string recipient, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            await audit.LogAsync(
                actor.UserId, AuditAction.ConferenceInviteReachedNobody, "Incident", incidentId.ToString(),
                description: $"{recipient} asked to be brought into a conference for this incident, and nobody was invited.",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not write the audit row for a conference invite that reached nobody on incident {IncidentId}",
                incidentId);
        }
    }

    private async Task HandOnToTheNextStepAsync(
        Guid incidentId, Actor actor, string recipient, CancellationToken cancellationToken)
    {
        bool pagedSomeone;
        try
        {
            using var scope = serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IEscalationOrchestrator>();
            pagedSomeone = await orchestrator.EscalateNowAsync(incidentId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Responder-initiated escalation for incident {IncidentId} could not be advanced, so the keypress paged nobody. "
                + "The keypress itself is recorded and escalation state is untouched.",
                incidentId);
            pagedSomeone = false;
        }

        if (pagedSomeone)
        {
            logger.LogInformation(
                "Responder-initiated escalation paged the next step of incident {IncidentId}.", incidentId);
            return;
        }

        // The confirmation was played and the call was hung up before this ran, so the responder has
        // already been told escalation was under way. Only the timeline can correct that.
        logger.LogWarning(
            "Responder-initiated escalation for incident {IncidentId} PAGED NOBODY. {Recipient} was told on the call "
            + "that the escalation had started; unless somebody is paged by hand, nobody else is coming.",
            incidentId, PiiRedactor.Recipient(recipient));

        await AppendTimelineAsync(
            incidentId,
            TimelineEventType.CallEscalated,
            "Escalation requested on the call paged nobody",
            $"{recipient} pressed to escalate and the call told them it had started, but there was no step left to "
            + "hand on to. Nobody else has been paged — escalate this incident by hand.",
            actor,
            cancellationToken);
    }

    private async Task BringTheResponderInAsync(
        Guid incidentId, Actor actor, string recipient, CancellationToken cancellationToken)
    {
        ConferenceRoomResult result;
        try
        {
            using var scope = serviceProvider.CreateScope();
            var conferences = scope.ServiceProvider.GetRequiredService<IVideoConferenceService>();
            result = await conferences.CreateRoomAsync(incidentId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not create the conference room {Recipient} asked for on the call for incident {IncidentId}",
                PiiRedactor.Recipient(recipient), incidentId);
            result = new ConferenceRoomResult { Success = false, Error = ex.Message };
        }

        if (!result.Success)
        {
            await AppendTimelineAsync(
                incidentId,
                TimelineEventType.ConferenceCreated,
                "Conference requested on the call could not be created",
                $"{recipient} asked to be brought in, but no conference room could be created ({result.Error}). "
                + "They have not been sent a link and they are not connected to anything.",
                actor,
                cancellationToken);
            return;
        }

        if (result.InvitesSentCount == 0)
        {
            await AppendTimelineAsync(
                incidentId,
                TimelineEventType.ConferenceCreated,
                "Conference opened, but nobody was invited",
                $"{recipient} asked to be brought in. The call had already ended, and nobody was sent a join "
                + "link — the incident has no team with a reachable contact, or no SMS/email channel is "
                + "configured. Share the conference link with the team by hand.",
                actor,
                cancellationToken);

            await WriteNobodyInvitedAuditAsync(incidentId, actor, recipient, cancellationToken);
            return;
        }

        var invited = actor.UserId is not null
                      && await WasInvitedAsync(result.RoomId, actor.UserId, cancellationToken);

        var reach = invited
            ? "The join link has been sent to them and to the rest of the incident's team."
            : actor.UserId is null
                ? "The join link went to the incident's team; the number that was called does not identify one user, "
                  + "so whether the person on the call can reach it is unknown."
                : "The join link went to the incident's team, which they are not part of — they have NOT been sent one.";

        await AppendTimelineAsync(
            incidentId,
            TimelineEventType.ConferenceCreated,
            "Conference opened for a request made on the call",
            $"{recipient} asked to be brought in. The call had already ended, so they were not bridged onto the "
            + $"conference. {reach}",
            actor,
            cancellationToken);
    }

    private async Task<bool> WasInvitedAsync(Guid roomId, string userId, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.ConferenceParticipants
                .AsNoTracking()
                .AnyAsync(p => p.ConferenceRoomId == roomId && p.UserId == userId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not check who was invited to conference room {RoomId}", roomId);
            return false;
        }
    }

    /// <summary>Records an outcome that is only known after the keypress was committed.</summary>
    private async Task AppendTimelineAsync(
        Guid incidentId,
        TimelineEventType eventType,
        string title,
        string description,
        Actor actor,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

            context.Set<IncidentTimelineEvent>().Add(new IncidentTimelineEvent
            {
                IncidentId = incidentId,
                EventType = eventType,
                Title = title,
                Description = description,
                ActorUserId = actor.UserId,
                ActorName = actor.DisplayName,
                CreatedAt = DateTime.UtcNow
            });

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not record on incident {IncidentId} what a phone keypress achieved: {Title}", incidentId, title);
        }
    }
}
