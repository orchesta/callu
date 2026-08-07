using Callu.Api.Controllers;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Conference;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// The endpoint callu-voice posts call status to. The service authenticates with nothing and retries
/// the same body, so everything here is about what the sealed token in the URL allows and what a
/// repeated delivery is allowed to change.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CalluVoiceCallbackTests(PostgresFixture pg)
{
    private const string Phone = "+905321234567";
    private const string CallId = "call-1";

    // ------------------------------------------------------------------ what the token allows

    [PostgresFact]
    public async Task AValidToken_IsAcceptedAndBoundToTheCallItWasMintedFor()
    {
        var world = await ArrangeAsync();

        var response = await PostAsync(world, world.Token, "acknowledged");

        Assert.IsType<OkObjectResult>(response);

        var call = await SingleCallAsync(world);
        Assert.Equal(CallId, call.CallToken);
        Assert.Equal(world.IncidentId, call.IncidentId);
        Assert.Equal(Phone, call.PhoneNumber);
        Assert.Equal(CallStatus.Acknowledged, call.Status);
    }

    /// <summary>The body names the call too, and it is unauthenticated; it may not redirect the callback.</summary>
    [PostgresFact]
    public async Task ATokenMintedForAnotherCall_IsRefusedAndChangesNothing()
    {
        var world = await ArrangeAsync();
        var otherCall = world.Tokens.Issue(world.IncidentId, "some-other-call", Phone);

        var response = await PostAsync(world, otherCall, "acknowledged");

        Assert.IsType<UnauthorizedObjectResult>(response);
        await AssertNothingHappenedAsync(world);
    }

    /// <summary>An unauthenticated acknowledgement stops every remaining page, so it has to be refused.</summary>
    [PostgresFact]
    public async Task NoUsableToken_IsRefusedAndTheIncidentKeepsPaging()
    {
        var world = await ArrangeAsync();

        foreach (var token in new[] { "", "   ", "not-a-token", ForeignInstallationToken(world.IncidentId) })
        {
            var response = await PostAsync(world, token, "acknowledged");
            Assert.IsType<UnauthorizedObjectResult>(response);
        }

        await AssertNothingHappenedAsync(world);
    }

    // ------------------------------------------------------------------ the same delivery twice

    /// <summary>callu-voice retries the same body up to three times; a repeat must not be applied twice.</summary>
    [PostgresFact]
    public async Task TheSameCallAndStatusDeliveredTwice_WritesOneOutcome()
    {
        var world = await ArrangeAsync();

        Assert.IsType<OkObjectResult>(await PostAsync(world, world.Token, "acknowledged"));
        var first = await SingleCallAsync(world);
        var acknowledgedAt = await AcknowledgedAtAsync(world);

        Assert.IsType<OkObjectResult>(await PostAsync(world, world.Token, "acknowledged"));

        var second = await SingleCallAsync(world);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.CompletedAt, second.CompletedAt);
        Assert.Equal(acknowledgedAt, await AcknowledgedAtAsync(world));

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Single(await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == AuditAction.Acknowledged).ToListAsync());
        Assert.Single(await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallAcknowledged).ToListAsync());
    }

    /// <summary>The callback's dedupe is a read followed by an insert, so the schema has to be what makes it true.</summary>
    [PostgresFact]
    public async Task TwoLiveCallLogs_CannotShareOneCallToken()
    {
        var world = await ArrangeAsync();

        Assert.IsType<OkObjectResult>(await PostAsync(world, world.Token, "answered"));

        await using var db = PostgresFixture.Context(world.ConnectionString);
        db.CallLogs.Add(new CallLog
        {
            IncidentId = world.IncidentId,
            PhoneNumber = Phone,
            CallToken = CallId,
            InitiatedAt = DateTime.UtcNow
        });

        // Without the index both rows persist, and every later read of the call picks one of them at random.
        var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", (conflict.InnerException as Npgsql.PostgresException)?.SqlState);
    }

    /// <summary>A retried progress callback is not news; applying it again duplicates the incident's history.</summary>
    [PostgresFact]
    public async Task ARepeatedProgressCallback_IsNotAppliedASecondTime()
    {
        var world = await ArrangeAsync();

        Assert.Equal(CalluVoiceCallbackApplication.Applied, await ApplyAsync(world, "alerting"));
        Assert.Equal(CalluVoiceCallbackApplication.AlreadyApplied, await ApplyAsync(world, "alerting"));

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Single(await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallInitiated).ToListAsync());
    }

    /// <summary>A repeat of the status already stored is a retry; a different status after the call ended is not.</summary>
    [PostgresFact]
    public async Task ARetryAndALateCallbackAreToldApart()
    {
        var world = await ArrangeAsync();

        Assert.Equal(CalluVoiceCallbackApplication.Applied, await ApplyAsync(world, "acknowledged"));
        Assert.Equal(CalluVoiceCallbackApplication.AlreadyApplied, await ApplyAsync(world, "acknowledged"));
        Assert.Equal(CalluVoiceCallbackApplication.OutOfOrder, await ApplyAsync(world, "connected"));
    }

    /// <summary>A queued progress callback can outlive the call; applying it would undo the outcome.</summary>
    [PostgresFact]
    public async Task AProgressCallbackArrivingAfterTheCallEnded_DoesNotUndoTheOutcome()
    {
        var world = await ArrangeAsync();

        await PostAsync(world, world.Token, "no_answer");
        var armed = await SingleCallAsync(world);

        Assert.IsType<OkObjectResult>(await PostAsync(world, world.Token, "connected"));

        var after = await SingleCallAsync(world);
        Assert.Equal(CallStatus.NoAnswer, after.Status);
        Assert.Equal(armed.CompletedAt, after.CompletedAt);
        Assert.Equal(armed.NextRetryAt, after.NextRetryAt);
    }

    /// <summary>One call settles once; a second terminal status must not rewrite what happened on it.</summary>
    [PostgresFact]
    public async Task ASecondTerminalStatusForOneCall_DoesNotRewriteTheAcknowledgement()
    {
        var world = await ArrangeAsync();

        await PostAsync(world, world.Token, "acknowledged");
        await PostAsync(world, world.Token, "failed");

        var call = await SingleCallAsync(world);
        Assert.Equal(CallStatus.Acknowledged, call.Status);
        Assert.Null(call.NextRetryAt);
        Assert.Equal(IncidentStatus.Acknowledged, await IncidentStatusAsync(world));
    }

    // ------------------------------------------------------------------ the status table, applied

    /// <summary>Every status callu-voice can send, and what it leaves behind.</summary>
    [PostgresTheory]
    [InlineData("alerting", CallStatus.Initiated, false, false, IncidentStatus.Open)]
    [InlineData("connected", CallStatus.Connected, false, false, IncidentStatus.Open)]
    [InlineData("acknowledged", CallStatus.Acknowledged, true, false, IncidentStatus.Acknowledged)]
    [InlineData("escalated", CallStatus.Escalated, true, false, IncidentStatus.Open)]
    [InlineData("conference_requested", CallStatus.ConferenceRequested, true, false, IncidentStatus.Acknowledged)]
    [InlineData("voicemail", CallStatus.Voicemail, true, true, IncidentStatus.Open)]
    [InlineData("no_answer", CallStatus.NoAnswer, true, true, IncidentStatus.Open)]
    [InlineData("silence_timeout", CallStatus.SilenceTimeout, true, true, IncidentStatus.Open)]
    [InlineData("timeout", CallStatus.Timeout, true, true, IncidentStatus.Open)]
    [InlineData("failed", CallStatus.Failed, true, true, IncidentStatus.Open)]
    // Not a healthy state: an unknown status closes the leg and leaves the chain paging.
    [InlineData("something_added_later", CallStatus.Failed, true, true, IncidentStatus.Open)]
    public async Task EachStatus_WritesTheCallLogAndTheIncidentEffectItMeans(
        string status, CallStatus expected, bool endsTheCall, bool keepsPaging, IncidentStatus incidentStatus)
    {
        var world = await ArrangeAsync();

        await PostAsync(world, world.Token, status);

        var call = await SingleCallAsync(world);
        Assert.Equal(expected, call.Status);
        Assert.Equal(endsTheCall, call.CompletedAt is not null);
        Assert.Equal(keepsPaging, call.NextRetryAt is not null);
        Assert.Equal(incidentStatus, await IncidentStatusAsync(world));
    }

    /// <summary>Without an armed row the retry sweep never re-dials, so one ring is all a responder gets.</summary>
    [PostgresFact]
    public async Task AnUnansweredCall_ArmsTheRowTheRetrySweepSelectsOn()
    {
        var world = await ArrangeAsync();

        await PostAsync(world, world.Token, "no_answer");

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var due = await db.CallLogs.AsNoTracking()
            .Where(c => c.NextRetryAt != null && !c.IsDeleted)
            .ToListAsync();

        var row = Assert.Single(due);
        Assert.Equal(1, row.AttemptNumber);
        Assert.InRange(row.NextRetryAt!.Value, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(2));
    }

    [PostgresFact]
    public async Task AnAcknowledgement_StandsDownEveryArmedRetryForTheIncident()
    {
        var world = await ArrangeAsync();
        await ArmAnotherRespondersRetryAsync(world);

        await PostAsync(world, world.Token, "acknowledged");

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Empty(await db.CallLogs.AsNoTracking().Where(c => c.NextRetryAt != null).ToListAsync());
    }

    // ------------------------------------------------------------------ who pressed the key

    [PostgresFact]
    public async Task TheAcknowledgement_IsAuditedToTheMatchedUsersId()
    {
        var world = await ArrangeAsync();

        await PostAsync(world, world.Token, "acknowledged");

        var row = await SingleAuditRowAsync(world.ConnectionString, AuditAction.Acknowledged);
        Assert.Equal("Incident", row.ResourceType);
        Assert.Equal(world.IncidentId, row.ResourceId);
        Assert.Equal(world.UserId, row.ActorId);
        Assert.Equal("Status: Open", row.ChangeBefore);
        Assert.Equal("Status: Acknowledged", row.ChangeAfter);
        Assert.Contains("keypress", row.Summary ?? "", StringComparison.OrdinalIgnoreCase);

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == world.IncidentId);
        Assert.Equal(world.UserId, incident.AcknowledgedBy);
    }

    /// <summary>Two people sharing a phone number means the actor is unknown, not arbitrary.</summary>
    [PostgresFact]
    public async Task WhenThePhoneMatchesTwoUsers_TheRowCarriesNoActorRatherThanTheWrongOne()
    {
        var world = await ArrangeAsync(secondUserOnTheSameNumber: true);

        await PostAsync(world, world.Token, "acknowledged");

        var row = await SingleAuditRowAsync(world.ConnectionString, AuditAction.Acknowledged);
        Assert.True(string.IsNullOrEmpty(row.ActorId),
            $"the acknowledgement was attributed to '{row.ActorId}' though two users share the number");

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var timeline = await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .FirstAsync(t => t.EventType == TimelineEventType.CallAcknowledged);
        Assert.True(string.IsNullOrEmpty(timeline.ActorUserId));
    }

    [PostgresFact]
    public async Task EveryKeypress_ReachesTheAuditTrail()
    {
        foreach (var (status, action) in new[]
                 {
                     ("acknowledged", AuditAction.Acknowledged),
                     ("escalated", AuditAction.Escalated),
                     ("conference_requested", AuditAction.Acknowledged)
                 })
        {
            var world = await ArrangeAsync();

            await PostAsync(world, world.Token, status);

            var row = await SingleAuditRowAsync(world.ConnectionString, action);
            Assert.Equal(world.UserId, row.ActorId);
        }
    }

    /// <summary>A call that only rang changed nothing, so it writes no audit row.</summary>
    [PostgresFact]
    public async Task ACallThatOnlyRang_WritesNoAuditRow()
    {
        var world = await ArrangeAsync();

        await PostAsync(world, world.Token, "alerting");

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Empty(await db.Set<AuditLog>().AsNoTracking().ToListAsync());
    }

    // ------------------------------------------------------------------ pressing 9

    /// <summary>callu-voice has hung up by the time the room exists, so nothing may say the responder is on it.</summary>
    [PostgresFact]
    public async Task AConferenceRequest_OpensARoomWithoutClaimingTheResponderWasConnected()
    {
        var world = await ArrangeAsync();
        var roomId = await CreateRoomWithParticipantAsync(world, world.UserId);
        world.Conferences.CreateRoomAsync(world.IncidentId, Arg.Any<CancellationToken>())
            .Returns(new ConferenceRoomResult { Success = true, RoomId = roomId, ParticipantCount = 1 });

        await PostAsync(world, world.Token, "conference_requested");

        var call = await SingleCallAsync(world);
        Assert.Equal(CallStatus.ConferenceRequested, call.Status);
        Assert.NotEqual(CallStatus.ConferenceCreated, call.Status);

        // Both entries: the keypress as it was recorded, and what opening the room actually achieved.
        var asked = await TimelineEntryAsync(world, "Responder asked to be brought in");
        Assert.Contains("not bridged", asked, StringComparison.OrdinalIgnoreCase);

        var opened = await TimelineEntryAsync(world, "Conference opened for a request made on the call");
        Assert.Contains("not bridged", opened, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sent to them", opened, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The conference key takes the incident too, and the trail has to say who took it and when.</summary>
    // Its own event is about the room, so without a line of its own the ownership change shows up
    // only in the incident's fields — a postmortem reading the timeline cannot see it happen.
    [PostgresFact]
    public async Task AConferenceRequest_RecordsTheAcknowledgementItAlsoPerforms()
    {
        var world = await ArrangeAsync();
        var roomId = await CreateRoomWithParticipantAsync(world, world.UserId);
        world.Conferences.CreateRoomAsync(world.IncidentId, Arg.Any<CancellationToken>())
            .Returns(new ConferenceRoomResult { Success = true, RoomId = roomId, ParticipantCount = 1 });

        await PostAsync(world, world.Token, "conference_requested");

        Assert.Equal(IncidentStatus.Acknowledged, await IncidentStatusAsync(world));

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var acknowledgement = await db.Set<IncidentTimelineEvent>()
            .AsNoTracking()
            .Where(e => e.IncidentId == world.IncidentId
                        && e.EventType == TimelineEventType.CallAcknowledged)
            .ToListAsync();

        Assert.Single(acknowledgement);
        Assert.Contains("took the incident", acknowledgement[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A responder who is not on the incident's team gets no invite, and the timeline has to say so.</summary>
    [PostgresFact]
    public async Task AConferenceRequestFromSomeoneOutsideTheTeam_SaysTheyWereNotSentALink()
    {
        var world = await ArrangeAsync();
        var roomId = await CreateRoomWithParticipantAsync(world, "somebody-else");
        world.Conferences.CreateRoomAsync(world.IncidentId, Arg.Any<CancellationToken>())
            .Returns(new ConferenceRoomResult { Success = true, RoomId = roomId, ParticipantCount = 1 });

        await PostAsync(world, world.Token, "conference_requested");

        var opened = await TimelineEntryAsync(world, "Conference opened for a request made on the call");
        Assert.Contains("NOT been sent", opened, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>No team, or a team with nobody reachable: the invite reached zero people, and that has to say so — not read as if the team simply got it.</summary>
    [PostgresFact]
    public async Task AConferenceRequestThatInvitedNobody_SaysSoAndWritesAnAuditRow()
    {
        var world = await ArrangeAsync();
        world.Conferences.CreateRoomAsync(world.IncidentId, Arg.Any<CancellationToken>())
            .Returns(new ConferenceRoomResult
            {
                Success = true,
                RoomId = Guid.NewGuid(),
                ParticipantCount = 0,
                InvitesSentCount = 0
            });

        await PostAsync(world, world.Token, "conference_requested");

        var opened = await TimelineEntryAsync(world, "Conference opened, but nobody was invited");
        Assert.Contains("nobody", opened, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sent to them", opened, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("which they are not part of", opened, StringComparison.OrdinalIgnoreCase);

        var row = await SingleAuditRowAsync(world.ConnectionString, AuditAction.ConferenceInviteReachedNobody);
        Assert.Equal(world.IncidentId, row.ResourceId);
    }

    [PostgresFact]
    public async Task AConferenceThatCouldNotBeCreated_SaysNobodyWasConnected()
    {
        var world = await ArrangeAsync();
        world.Conferences.CreateRoomAsync(world.IncidentId, Arg.Any<CancellationToken>())
            .Returns(new ConferenceRoomResult { Success = false, Error = "Incident not found" });

        await PostAsync(world, world.Token, "conference_requested");

        var failed = await TimelineEntryAsync(world, "Conference requested on the call could not be created");
        Assert.Contains("not connected", failed, StringComparison.OrdinalIgnoreCase);

        // The responder still took the incident, whatever happened to the conference.
        Assert.Equal(IncidentStatus.Acknowledged, await IncidentStatusAsync(world));
    }

    // ------------------------------------------------------------------ pressing 2

    /// <summary>The call promised the escalation before we knew; only the timeline can correct it.</summary>
    [PostgresFact]
    public async Task AnEscalationThatPagedNobody_SaysSoWhereAnOperatorLooks()
    {
        var world = await ArrangeAsync();
        world.Escalation.EscalateNowAsync(world.IncidentId, Arg.Any<CancellationToken>()).Returns(false);

        await PostAsync(world, world.Token, "escalated");

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var events = await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallEscalated)
            .ToListAsync();

        Assert.Contains(events, e => e.Title.Contains("paged nobody", StringComparison.OrdinalIgnoreCase));
    }

    [PostgresFact]
    public async Task AnEscalationThatPagedSomeone_LeavesNoCorrectionBehind()
    {
        var world = await ArrangeAsync();
        world.Escalation.EscalateNowAsync(world.IncidentId, Arg.Any<CancellationToken>()).Returns(true);

        await PostAsync(world, world.Token, "escalated");

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var events = await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallEscalated)
            .ToListAsync();

        Assert.DoesNotContain(events, e => e.Title.Contains("paged nobody", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ the incident is gone

    /// <summary>A late keypress on a closed incident is recorded, but it may not act on it.</summary>
    [PostgresFact]
    public async Task AKeypressOnAnIncidentThatIsAlreadyClosed_OpensNoConferenceAndIsStillRecorded()
    {
        var world = await ArrangeAsync(status: IncidentStatus.Closed);

        await PostAsync(world, world.Token, "conference_requested");

        Assert.Equal(IncidentStatus.Closed, await IncidentStatusAsync(world));
        await world.Conferences.DidNotReceive().CreateRoomAsync(world.IncidentId, Arg.Any<CancellationToken>());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == world.IncidentId);
        Assert.Equal("someone-else", incident.AcknowledgedBy);

        var row = await SingleAuditRowAsync(world.ConnectionString, AuditAction.Acknowledged);
        Assert.Contains("Closed", row.ChangeAfter ?? "", StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- why a call reached nobody

    /// <summary>The hangup cause is all that separates a busy line from a trunk refusing every dial.</summary>
    [PostgresFact]
    public async Task TheHangupCause_ReachesTheCallLogAndTheTimeline()
    {
        var world = await ArrangeAsync();

        await world.Persistence.ProcessAsync(
            new CalluVoiceCallbackTicket(world.IncidentId, CallId, Phone),
            new CalluVoiceCallbackRequest
            {
                CallId = CallId,
                Status = "no_answer",
                Data = new Dictionary<string, string> { ["cause"] = "CHANUNAVAIL" }
            },
            CancellationToken.None);

        Assert.Equal("CHANUNAVAIL", (await SingleCallAsync(world)).FailureReason);

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var timeline = await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .SingleAsync(t => t.EventType == TimelineEventType.CallFailed);
        Assert.Contains("CHANUNAVAIL", timeline.Description ?? "", StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- harness

    private static Task<IActionResult> PostAsync(World world, string? token, string status) =>
        world.Controller.ReceiveCallback(
            token ?? "",
            new CalluVoiceCallbackRequest
            {
                CallId = CallId,
                Status = status,
                DurationSeconds = 12.5,
                Data = new Dictionary<string, string> { ["method"] = "dtmf", ["digit"] = "1" },
                Timestamp = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

    /// <summary>The same callback, applied straight at the writer so the verdict itself can be asserted.</summary>
    private static Task<CalluVoiceCallbackApplication> ApplyAsync(World world, string status) =>
        world.Persistence.ProcessAsync(
            new CalluVoiceCallbackTicket(world.IncidentId, CallId, Phone),
            new CalluVoiceCallbackRequest { CallId = CallId, Status = status, DurationSeconds = 12.5 },
            CancellationToken.None);

    private static string ForeignInstallationToken(Guid incidentId) =>
        new CalluVoiceCallbackTokenProtector(new EphemeralDataProtectionProvider())
            .Issue(incidentId, CallId, Phone)!;

    private static async Task<CallLog> SingleCallAsync(World world)
    {
        await using var db = PostgresFixture.Context(world.ConnectionString);
        return Assert.Single(await db.CallLogs.AsNoTracking()
            .Where(c => c.CallToken == CallId).ToListAsync());
    }

    private static async Task<IncidentStatus> IncidentStatusAsync(World world)
    {
        await using var db = PostgresFixture.Context(world.ConnectionString);
        return (await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == world.IncidentId)).Status;
    }

    private static async Task<DateTime?> AcknowledgedAtAsync(World world)
    {
        await using var db = PostgresFixture.Context(world.ConnectionString);
        return (await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == world.IncidentId)).AcknowledgedAt;
    }

    /// <summary>The description of the one timeline entry carrying this exact title.</summary>
    private static async Task<string> TimelineEntryAsync(World world, string title)
    {
        await using var db = PostgresFixture.Context(world.ConnectionString);
        var events = await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.Title == title)
            .ToListAsync();

        var found = Assert.Single(events);
        return found.Description ?? "";
    }

    private static async Task AssertNothingHappenedAsync(World world)
    {
        await using var db = PostgresFixture.Context(world.ConnectionString);

        Assert.Empty(await db.CallLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Set<AuditLog>().AsNoTracking().ToListAsync());
        Assert.Empty(await db.Set<IncidentTimelineEvent>().AsNoTracking().ToListAsync());
        Assert.Equal(IncidentStatus.Open,
            (await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == world.IncidentId)).Status);
    }

    private static async Task ArmAnotherRespondersRetryAsync(World world)
    {
        await using var db = PostgresFixture.Context(world.ConnectionString);
        db.CallLogs.Add(new CallLog
        {
            IncidentId = world.IncidentId,
            PhoneNumber = "+905554445566",
            Status = CallStatus.NoAnswer,
            AttemptNumber = 1,
            InitiatedAt = DateTime.UtcNow.AddMinutes(-2),
            CompletedAt = DateTime.UtcNow.AddMinutes(-1),
            NextRetryAt = DateTime.UtcNow.AddSeconds(30),
            CallToken = "another-call"
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CreateRoomWithParticipantAsync(World world, string userId)
    {
        await using var db = PostgresFixture.Context(world.ConnectionString);

        var room = new ConferenceRoom
        {
            IncidentId = world.IncidentId,
            RoomToken = Guid.NewGuid().ToString("N"),
            Status = ConferenceRoomStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60)
        };
        db.ConferenceRooms.Add(room);
        await db.SaveChangesAsync();

        db.ConferenceParticipants.Add(new ConferenceParticipant
        {
            ConferenceRoomId = room.Id,
            UserId = userId,
            ParticipantToken = Guid.NewGuid().ToString("N"),
            DisplayName = userId
        });
        await db.SaveChangesAsync();

        return room.Id;
    }

    private static async Task<AuditLog> SingleAuditRowAsync(string connectionString, AuditAction action)
    {
        await using var db = PostgresFixture.Context(connectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == action)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync();

        Assert.True(rows.Count == 1, $"expected exactly one {action} row, found {rows.Count}");
        return rows[0];
    }

    private sealed record World(
        string ConnectionString,
        Guid IncidentId,
        string UserId,
        string Token,
        CalluVoiceCallbackTokenProtector Tokens,
        CalluVoiceCallbackController Controller,
        ICalluVoiceCallbackPersistence Persistence,
        IEscalationOrchestrator Escalation,
        IVideoConferenceService Conferences);

    private async Task<World> ArrangeAsync(
        IncidentStatus status = IncidentStatus.Open,
        bool secondUserOnTheSameNumber = false)
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid incidentId;
        string userId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var responder = new ApplicationUser
            {
                Id = "responder-1",
                UserName = "ada",
                Email = "ada@example.com",
                PhoneNumber = Phone,
                FirstName = "Ada",
                LastName = "Çelik",
            };
            db.Users.Add(responder);
            userId = responder.Id;

            if (secondUserOnTheSameNumber)
            {
                db.Users.Add(new ApplicationUser
                {
                    Id = "responder-2",
                    UserName = "mert",
                    Email = "mert@example.com",
                    PhoneNumber = Phone,
                });
            }

            var incident = new Incident
            {
                Title = "Checkout failing",
                Severity = IncidentSeverity.Critical,
                Status = status,
                StartedAt = DateTime.UtcNow,
                AcknowledgedAt = status == IncidentStatus.Open ? null : DateTime.UtcNow,
                AcknowledgedBy = status == IncidentStatus.Open ? null : "someone-else",
            };
            db.Add(incident);
            await db.SaveChangesAsync();
            incidentId = incident.Id;
        }

        var escalation = Substitute.For<IEscalationOrchestrator>();
        escalation.EscalateNowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var conferences = Substitute.For<IVideoConferenceService>();
        conferences.CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ConferenceRoomResult { Success = true, RoomId = Guid.NewGuid() });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        // The callback resolves this out of its own scope; without it the audit write is swallowed by
        // the catch that keeps a failing audit sink from breaking a live call.
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton(escalation);
        services.AddSingleton(conferences);
        var provider = services.BuildServiceProvider();

        var dataProtection = new EphemeralDataProtectionProvider();
        var tokens = new CalluVoiceCallbackTokenProtector(dataProtection);

        var persistence = provider.GetRequiredService<ICalluVoiceCallbackPersistence>();
        var controller = new CalluVoiceCallbackController(
            persistence, dataProtection, NullLogger<CalluVoiceCallbackController>.Instance);

        return new World(
            cs, incidentId, userId, tokens.Issue(incidentId, CallId, Phone)!, tokens,
            controller, persistence, escalation, conferences);
    }
}
