using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Callu.Tests;

/// <summary>
/// IncidentTimelineEvent.Description clamps like its two sibling columns, against a real PostgreSQL
/// varchar(1000).
/// </summary>
[Collection(PostgresCollection.Name)]
public class TimelineDescriptionOverflowTests(PostgresFixture pg)
{
    // Read off the entity, so a widened column moves this file with it.
    private static readonly int MaxDescriptionLength =
        typeof(IncidentTimelineEvent)
            .GetProperty(nameof(IncidentTimelineEvent.Description), BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttribute<StringLengthAttribute>()!
            .MaximumLength;

    // Built the way EscalationOrchestrator.WriteChannelsSilentTimelineAsync builds it.
    private static string ChannelsSilentDescription(int teamSize)
    {
        var silences = Enumerable.Range(1, teamSize)
            .SelectMany(i => new[]
            {
                new DispatchChannelSilence(
                    $"responder-{i:D2}@example.com", NotificationType.VoiceCall,
                    "no voice provider is registered"),
                new DispatchChannelSilence(
                    $"responder-{i:D2}@example.com", NotificationType.Email,
                    "SMTP is not configured")
            })
            .ToList();

        var dispatch = new NotificationDispatchResult(
            Reached: 0, Failed: 0, Silent: teamSize, ChannelSilences: silences);

        var target = $"Team: {Guid.NewGuid()} (all)";
        const string next = "The escalation will advance to the next step on the next tick.";

        return $"{target} — {dispatch.Silent} responder(s) were on-call, but not one of their notification "
             + $"channels could send, so nobody was contacted: {dispatch.DescribeChannelSilences()}. "
             + $"This is NOT an empty on-call rota — the responders are there and the schedule is fine. "
             + $"Configure the channel(s) above (a voice provider, SMTP) or nobody on this step can be paged. {next}";
    }

    [Fact]
    public void TheEscalationsOwnMessage_ReallyDoesOverflowTheColumn()
    {
        var description = ChannelsSilentDescription(teamSize: 10);

        Assert.True(description.Length > MaxDescriptionLength,
            $"a ten-member team with no voice provider and no SMTP produced {description.Length} "
            + $"characters, which fits varchar({MaxDescriptionLength}) — either DescribeChannelSilences "
            + "got terser or the column got wider, and the overflow has to be re-derived before these "
            + "tests mean anything");

        Assert.True(ChannelsSilentDescription(teamSize: 6).Length > MaxDescriptionLength,
            "six responders no longer overflow — the threshold moved, so re-derive it rather than "
            + "assuming it is still a ten-member problem");
    }

    [PostgresFact]
    public async Task TheChannelsSilentMessage_DoesNotBlowUpSaveChanges()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var incidentId = await SeedIncidentAsync(cs);

        var description = ChannelsSilentDescription(teamSize: 10);

        await using var db = PostgresFixture.Context(cs);
        db.Add(new IncidentTimelineEvent
        {
            IncidentId = incidentId,
            EventType = TimelineEventType.Escalated,
            Title = "Escalation step 1: nobody could be paged (no channel could send)",
            Description = description,
            ActorUserId = "system"
        });

        await db.SaveChangesAsync();

        var stored = await db.IncidentTimelineEvents.AsNoTracking().SingleAsync();
        Assert.NotNull(stored.Description);
        Assert.True(stored.Description!.Length <= MaxDescriptionLength);

        // Truncated, not discarded: what the operator reads at 3am still names the target and the fault.
        Assert.StartsWith("Team: ", stored.Description);
        Assert.Contains("no voice provider is registered", stored.Description);
    }

    [PostgresFact]
    public async Task ClampingNeverSplitsASurrogatePair_SoTheRowStillEncodes()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var incidentId = await SeedIncidentAsync(cs);

        // One 'a' then surrogate PAIRS, so a pair straddles the boundary at the worst offset: the
        // last char that fits is a high surrogate whose partner does not.
        var description = "a" + string.Concat(Enumerable.Repeat("🔥", MaxDescriptionLength));

        await using var db = PostgresFixture.Context(cs);
        db.Add(new IncidentTimelineEvent
        {
            IncidentId = incidentId,
            EventType = TimelineEventType.Escalated,
            Title = "Escalation step 1 triggered",
            Description = description,
            ActorUserId = "system"
        });

        await db.SaveChangesAsync();

        var stored = await db.IncidentTimelineEvents.AsNoTracking().SingleAsync();
        Assert.True(stored.Description!.Length <= MaxDescriptionLength);
        Assert.False(char.IsHighSurrogate(stored.Description[^1]),
            "the description ends on a lone high surrogate — that is not valid UTF-8 and Postgres will "
            + "refuse it, which is the length error traded for an encoding error");
    }

    [PostgresFact]
    public async Task TheColumnStillRejectsAnOversizedDescription_SoTheClampIsLoadBearing()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var incidentId = await SeedIncidentAsync(cs);

        await using var db = PostgresFixture.Context(cs);

        var row = new IncidentTimelineEvent
        {
            IncidentId = incidentId,
            EventType = TimelineEventType.Escalated,
            Title = "Escalation step 1 triggered",
            Description = "short enough",
            ActorUserId = "system"
        };

        db.Add(row);
        await db.SaveChangesAsync();

        // Straight at the column, so this stays a statement about the DDL.
        var oversized = new string('x', MaxDescriptionLength + 1);

        var thrown = await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "IncidentTimelineEvents" SET "Description" = {oversized} WHERE "Id" = {row.Id}"""));

        Assert.Equal("22001", thrown.SqlState);
    }

    [Fact]
    public void BothSiblingColumns_AlreadyClampForExactlyThisReason()
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = "responder-1",
            Type = NotificationType.Email,
            Title = "t",
            Message = "m",
            DedupeKey = $"{Guid.NewGuid():N}"
        };
        notification.MarkFailed(new string('x', Notification.MaxErrorMessageLength + 500));

        Assert.True(notification.ErrorMessage!.Length <= Notification.MaxErrorMessageLength,
            "Notification.ErrorMessage stopped clamping — the sibling this guard argues from is gone");

        var delivery = new NotificationChannelDelivery
        {
            Error = new string('x', NotificationChannelDelivery.MaxErrorLength + 500)
        };

        Assert.True(delivery.Error!.Length <= NotificationChannelDelivery.MaxErrorLength,
            "NotificationChannelDelivery.Error stopped clamping — the other sibling is gone too");
    }

    private static async Task<Guid> SeedIncidentAsync(string connectionString)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var incident = new Incident
        {
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.Open,
            StartedAt = DateTime.UtcNow,
            IsEscalationActive = true,
            EscalationStartedAt = DateTime.UtcNow
        };

        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        return incident.Id;
    }
}
