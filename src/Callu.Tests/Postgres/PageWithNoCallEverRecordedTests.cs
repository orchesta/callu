using Callu.Application.Common.Interfaces;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Quartz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace Callu.Tests;

/// <summary>A voice page the provider accepted and never reported a call for.</summary>
// The provider answering "accepted" is the only thing that marks the page delivered; everything else
// about the call comes from a callback. When none arrives the incident shows no call at all, and the
// stuck sweep cannot see it because that reads call rows and there are none.
[Collection(PostgresCollection.Name)]
public class PageWithNoCallEverRecordedTests(PostgresFixture pg)
{
    private const string UserId = "responder-1";

    [PostgresFact]
    public async Task APageAcceptedButNeverReportedOn_IsPutOnTheTimeline()
    {
        var world = await ArrangeAsync(attemptedAgo: TimeSpan.FromMinutes(30));

        await world.Sweep.Execute(JobContext());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var timeline = Assert.Single(await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallFailed).ToListAsync());

        Assert.Equal("Voice call never confirmed", timeline.Title);
        Assert.Contains("nobody is confirmed to have been reached", timeline.Description ?? "", StringComparison.Ordinal);

        await world.Audit.Received(1).LogAsync(
            Arg.Any<string?>(), AuditAction.VoiceCallNeverConfirmed, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The sweep runs every couple of minutes; the same page may only be reported once.</summary>
    [PostgresFact]
    public async Task TheSamePage_IsNotReportedTwice()
    {
        var world = await ArrangeAsync(attemptedAgo: TimeSpan.FromMinutes(30));

        await world.Sweep.Execute(JobContext());
        await world.Sweep.Execute(JobContext());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Single(await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallFailed).ToListAsync());
    }

    /// <summary>A call IS on record for this page, so there is nothing to report.</summary>
    [PostgresFact]
    public async Task APageWhoseCallWasRecorded_IsLeftAlone()
    {
        var world = await ArrangeAsync(attemptedAgo: TimeSpan.FromMinutes(30), withCallLog: true);

        await world.Sweep.Execute(JobContext());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Empty(await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallFailed).ToListAsync());
    }

    /// <summary>A page dialled moments ago has not had time to report; reporting it would be noise.</summary>
    [PostgresFact]
    public async Task APageStillInsideTheGracePeriod_IsLeftAlone()
    {
        var world = await ArrangeAsync(attemptedAgo: TimeSpan.FromMinutes(1));

        await world.Sweep.Execute(JobContext());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Empty(await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallFailed).ToListAsync());
    }

    /// <summary>Older than the lookback, so it is history rather than something to act on.</summary>
    // The release that introduces this job finds every page ever sent with nothing recorded against
    // it. Without the window the first run writes a timeline event on every closed incident there is,
    // and none of them can be acted on now.
    [PostgresFact]
    public async Task APageOlderThanTheLookback_IsLeftAlone()
    {
        var world = await ArrangeAsync(
            attemptedAgo: UnconfirmedVoiceCallSweepQuartzJob.Lookback + TimeSpan.FromHours(1));

        await world.Sweep.Execute(JobContext());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Empty(await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallFailed).ToListAsync());

        await world.Audit.DidNotReceive().LogAsync(
            Arg.Any<string?>(), AuditAction.VoiceCallNeverConfirmed, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Just inside the window, so it is still the operator's to know about.</summary>
    [PostgresFact]
    public async Task APageJustInsideTheLookback_IsStillReported()
    {
        var world = await ArrangeAsync(
            attemptedAgo: UnconfirmedVoiceCallSweepQuartzJob.Lookback - TimeSpan.FromHours(1));

        await world.Sweep.Execute(JobContext());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Single(await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(t => t.EventType == TimelineEventType.CallFailed).ToListAsync());
    }

    // ---------------------------------------------------------------- harness

    private static IJobExecutionContext JobContext()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private sealed record World(
        string ConnectionString,
        UnconfirmedVoiceCallSweepQuartzJob Sweep,
        IAuditLogService Audit);

    private async Task<World> ArrangeAsync(TimeSpan attemptedAgo, bool withCallLog = false)
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var attemptedAt = DateTime.UtcNow.Subtract(attemptedAgo);

        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = new Incident
            {
                Title = "Search cluster unreachable",
                Severity = IncidentSeverity.Critical,
                Status = IncidentStatus.Open,
                StartedAt = attemptedAt
            };
            db.Add(incident);
            await db.SaveChangesAsync();

            var page = new Notification
            {
                UserId = UserId,
                Type = NotificationType.VoiceCall,
                Title = "Incident page",
                IncidentId = incident.Id,
                DeliveryStatus = NotificationDeliveryStatus.Delivered,
                LastAttemptAt = attemptedAt
            };
            db.Add(page);
            await db.SaveChangesAsync();

            if (withCallLog)
            {
                db.Add(new CallLog
                {
                    IncidentId = incident.Id,
                    PhoneNumber = "+905321234567",
                    AttemptId = page.Id,
                    Status = CallStatus.NoAnswer,
                    InitiatedAt = attemptedAt,
                    CompletedAt = attemptedAt
                });
                await db.SaveChangesAsync();
            }
        }

        var audit = Substitute.For<IAuditLogService>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        services.AddSingleton(audit);
        var provider = services.BuildServiceProvider();

        var sweep = new UnconfirmedVoiceCallSweepQuartzJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UnconfirmedVoiceCallSweepQuartzJob>.Instance);

        return new World(cs, sweep, audit);
    }
}
