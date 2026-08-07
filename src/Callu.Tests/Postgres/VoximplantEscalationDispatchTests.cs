using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Voximplant;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The phone path's escalation, asserted on what it writes and what payload it pages with.</summary>
// A payload field one path sets and the other forgets does not fail loudly — it drops the page as a duplicate.
[Collection(PostgresCollection.Name)]
public class VoximplantEscalationDispatchTests(PostgresFixture pg)
{
    /// <summary>The phone path's dedupe generation is the sweep's, compared against the sweep's own function.</summary>
    [PostgresFact]
    public async Task ResponderInitiatedEscalation_PagesWithTheSameDispatchGenerationAsTheSweep()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs);
        var (persistence, dispatcher, provider) = BuildPersistence(cs);
        await using var _ = provider;

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        var payload = Assert.Single(dispatcher.Payloads);

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        Assert.Equal(
            EscalationOrchestrator.ComputeDispatchGeneration(incident.EscalationStartedAt),
            payload.DispatchGeneration);
        Assert.NotEqual(0, payload.DispatchGeneration);
    }

    /// <summary>A second escalation run after a reopen must not collide with the first run's dedupe key.</summary>
    [PostgresFact]
    public async Task AfterAReopen_TheResponderInitiatedPage_DoesNotReuseThePreviousRunsDedupeKey()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs);
        var (persistence, dispatcher, provider) = BuildPersistence(cs);
        await using var _ = provider;

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        // Reopen clears EscalationStartedAt, then the sweep starts escalation run 2 from step 1.
        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = await db.Incidents.FirstAsync(i => i.Id == seed.IncidentId);
            incident.Acknowledge("someone");
            incident.Resolve("someone");
            incident.Reopen("someone");

            incident.EscalationPolicyId = seed.PolicyId;
            incident.IsEscalationActive = true;
            incident.EscalationStartedAt = DateTime.UtcNow;
            incident.CurrentEscalationStepId = seed.StepOneId;
            incident.LastEscalationStepAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-2"), null, NoopCallbacks());

        Assert.Equal(2, dispatcher.Payloads.Count);

        var first = dispatcher.Payloads[0];
        var second = dispatcher.Payloads[1];

        Assert.NotEqual(first.DispatchGeneration, second.DispatchGeneration);
        Assert.NotEqual(
            NotificationFactory.Create("responder-1", first, "/incidents/x", NotificationType.VoiceCall, first.DispatchGeneration).DedupeKey,
            NotificationFactory.Create("responder-1", second, "/incidents/x", NotificationType.VoiceCall, second.DispatchGeneration).DedupeKey);
    }

    /// <summary>The result reports what the keypress achieved, not that the callback was accepted.</summary>
    // The scenario picks its spoken prompt from this, so "escalation initiated" must not follow a page to nobody.
    [PostgresFact]
    public async Task PressTwo_ThatPagesNobody_ComesBackAsPagedNobody_NotAsSuccess()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs);
        var (persistence, dispatcher, provider) = BuildPersistence(cs);
        await using var _ = provider;

        // The keypress that works: step 2 exists, and somebody is paged.
        var paged = await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        Assert.True(paged.EscalationRequested);
        Assert.True(paged.EscalationPagedSomeone);
        Assert.False(paged.EscalationPagedNobody);

        // The policy is now exhausted. A second responder, on a second call, presses 2 — and there is
        // nobody left in the policy to hand the incident to.
        var nobody = await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-2"), null, NoopCallbacks());

        Assert.True(nobody.EscalationRequested);
        Assert.True(nobody.EscalationPagedNobody);

        // ...and that is the truth: not one extra page went out.
        Assert.Single(dispatcher.Payloads);
    }

    /// <summary>A step that ran into an empty rota reached nobody, just as an exhausted policy did.</summary>
    [PostgresFact]
    public async Task PressTwo_WhoseStepReachesAnEmptyRota_ComesBackAsPagedNobody()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs);

        // The step is paged, and the dispatch finds nobody on the other end of it.
        var dispatcher = new CapturingDispatcher { Outcome = () => NotificationDispatchResult.Nobody };
        var (persistence, _, provider) = BuildPersistence(cs, dispatcher);
        await using var __ = provider;

        var result = await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        Assert.True(result.EscalationPagedNobody);
        Assert.Single(dispatcher.Payloads);
    }

    /// <summary>An ordinary callback asks for no escalation, so it makes no claim about one either.</summary>
    [PostgresFact]
    public async Task ACallbackThatIsNotAKeypress_ClaimsNothingAboutEscalation()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs);
        var (persistence, _, provider) = BuildPersistence(cs);
        await using var _ = provider;

        var callback = EscalatedCallback(seed.IncidentId, "session-1");
        callback.Status = "acknowledged";

        var result = await persistence.ProcessAsync(callback, null, NoopCallbacks());

        Assert.False(result.EscalationRequested);
        Assert.False(result.EscalationPagedNobody);
    }

    /// <summary>The payload carries the incident's language and service, which the announcement is read from.</summary>
    [PostgresFact]
    public async Task ResponderInitiatedEscalation_CarriesTheIncidentsLanguageAndService()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs);
        var (persistence, dispatcher, provider) = BuildPersistence(cs);
        await using var _ = provider;

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        var payload = Assert.Single(dispatcher.Payloads);
        Assert.Equal("tr-TR", payload.DataLanguage);
        Assert.Equal("Payments", payload.ServiceName);
    }

    /// <summary>One keypress, one step: a replayed callback for the same session must not advance twice.</summary>
    // The scenario retries a callback whose ack it never saw, so the same terminal status can arrive twice.
    [PostgresFact]
    public async Task AReplayedEscalatedCallback_DoesNotAdvanceTheStepTwice()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs);
        var (persistence, dispatcher, provider) = BuildPersistence(cs);
        await using var _ = provider;

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());
        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        Assert.Equal(seed.StepTwoId, incident.CurrentEscalationStepId);
        Assert.Single(dispatcher.Payloads);

        var escalatedEvents = await db.Set<IncidentTimelineEvent>()
            .CountAsync(e => e.IncidentId == seed.IncidentId && e.EventType == TimelineEventType.CallEscalated);
        Assert.Equal(1, escalatedEvents);
    }

    // ---- the phone path is the orchestrator now: its failure modes are the orchestrator's ----------

    /// <summary>A step survives a failed dispatch, and the sweep pages that same step once the store recovers.</summary>
    [PostgresFact]
    public async Task PressTwo_WhoseDispatchFails_KeepsTheStep_AndTheSweepPagesItWhenTheStoreRecovers()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs);
        var dispatcher = new CapturingDispatcher { Outcome = () => new NotificationDispatchResult(0, 1) };
        var (persistence, _, provider) = BuildPersistence(cs, dispatcher);
        await using var _ = provider;

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

            // The step is still there to be paged — the pointer did NOT walk past it.
            Assert.Equal(seed.StepOneId, incident.CurrentEscalationStepId);
            Assert.True(incident.IsEscalationActive);

            // ...and the operator is told, on the timeline, rather than only in a log line that lied.
            Assert.True(await db.Set<IncidentTimelineEvent>().AnyAsync(e =>
                e.IncidentId == seed.IncidentId && e.Title.Contains("could not be queued (retrying)")));

            // The retry window is anchored on this first failure, not on how overdue the step was.
            Assert.NotNull(incident.DispatchFailingSince);
        }

        // The notification store recovers. The sweep pages the step the responder asked for.
        dispatcher.Outcome = null;
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IEscalationOrchestrator>()
                .ProcessPendingEscalationsAsync();

        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

            Assert.Equal(seed.StepTwoId, incident.CurrentEscalationStepId);
            Assert.Null(incident.DispatchFailingSince);
        }

        // Two attempts at step 2 (the failed press-2 page, then the sweep's), and step 2 is the one
        // paged both times — the step was never skipped.
        Assert.Equal(2, dispatcher.Payloads.Count);
        Assert.All(dispatcher.Payloads, p => Assert.Equal(2, p.EscalationLevel));
    }

    /// <summary>Advancing a step clears the dispatch-failure marker, so the next step gets its own window.</summary>
    // An inherited window is already spent, and the next step would give up on its very first failed page.
    [PostgresFact]
    public async Task PressTwo_ClearsTheDispatchFailureMarker_SoTheNextStepGetsItsOwnRetryWindow()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        // A previous step spent 20 minutes failing to dispatch before this one.
        var seed = await SeedEscalatingIncidentAsync(cs, dispatchFailingSince: DateTime.UtcNow.AddMinutes(-20));
        var (persistence, dispatcher, provider) = BuildPersistence(cs);
        await using var _ = provider;

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        Assert.Single(dispatcher.Payloads);

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        Assert.Equal(seed.StepTwoId, incident.CurrentEscalationStepId);
        Assert.Null(incident.DispatchFailingSince);
    }

    /// <summary>A page that reached someone but lost a channel is recorded rather than reported as success.</summary>
    // Nothing retries a rejected claim, so that responder's phone never rings and only the timeline can say so.
    [PostgresFact]
    public async Task PressTwo_WithAPartiallyLostPage_RecordsIt_InsteadOfReportingSuccess()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs);
        var dispatcher = new CapturingDispatcher
        {
            Outcome = () => new NotificationDispatchResult(
                1, 0, [new DispatchChannelFailure("responder-2", NotificationType.VoiceCall)])
        };
        var (persistence, _, provider) = BuildPersistence(cs, dispatcher);
        await using var _ = provider;

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        // A page did go out, so the step advances...
        Assert.Equal(seed.StepTwoId, incident.CurrentEscalationStepId);

        // ...and the lost voice call is in front of the operator rather than swallowed.
        var lost = await db.Set<IncidentTimelineEvent>()
            .FirstOrDefaultAsync(e => e.IncidentId == seed.IncidentId
                                   && e.Title.Contains("some pages could not be queued"));
        Assert.NotNull(lost);
        Assert.Contains("VoiceCall", lost!.Description);
    }

    /// <summary>An escalation that never ran starts a fresh run at step 1, with its own dedupe generation.</summary>
    // Nobody has been paged under this policy yet, so starting at step 1 cannot re-page anyone.
    [PostgresFact]
    public async Task PressTwo_OnAnEscalationThatNeverRan_StartsAFreshRun_AndPagesStepOneImmediately()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs, escalationActive: false, escalationEverStarted: false);
        var (persistence, dispatcher, provider) = BuildPersistence(cs);
        await using var _ = provider;

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        Assert.True(incident.IsEscalationActive);
        Assert.Equal(seed.StepOneId, incident.CurrentEscalationStepId);

        var payload = Assert.Single(dispatcher.Payloads);
        Assert.Equal(1, payload.EscalationLevel);
        Assert.Equal(
            EscalationOrchestrator.ComputeDispatchGeneration(incident.EscalationStartedAt),
            payload.DispatchGeneration);
    }

    /// <summary>A run whose step pointer is gone pages nobody rather than rewinding to step 1.</summary>
    // Restarting would re-dial everyone the run already paged, the responder holding the phone first.
    [PostgresFact]
    public async Task PressTwo_WhenTheRunsPointerIsGone_PagesNobody_RatherThanRestartAtStepOne()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedEscalatingIncidentAsync(cs, escalationActive: false);
        var (persistence, dispatcher, provider) = BuildPersistence(cs);
        await using var _ = provider;

        await persistence.ProcessAsync(EscalatedCallback(seed.IncidentId, "session-1"), null, NoopCallbacks());

        Assert.Empty(dispatcher.Payloads);

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        Assert.False(incident.IsEscalationActive);
        Assert.Null(incident.CurrentEscalationStepId);

        Assert.True(await db.Set<IncidentTimelineEvent>().AnyAsync(e =>
            e.IncidentId == seed.IncidentId && e.Title.Contains("could not be resumed")));
    }

    private static VoxCallbackRequest EscalatedCallback(Guid incidentId, string session) =>
        new()
        {
            IncidentId = incidentId.ToString(),
            CallSessionId = session,
            Status = "escalated",
            Duration = 12,
            Data = new Dictionary<string, object> { ["phone"] = "+905551112233" }
        };

    private static VoximplantCallbackProcessingCallbacks NoopCallbacks() =>
        new(
            (_, _) => Task.FromResult<VoxCallData?>(null),
            (_, _) => Task.FromResult(true),
            _ => Task.CompletedTask);

    private static (VoximplantVoiceCallbackPersistence Persistence, CapturingDispatcher Dispatcher, ServiceProvider Provider)
        BuildPersistence(string connectionString, CapturingDispatcher? dispatcher = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);

        dispatcher ??= new CapturingDispatcher();
        services.AddSingleton<INotificationDispatcher>(dispatcher);

        // The phone path no longer pages anyone itself — it hands the incident to the one
        // escalation implementation, so the real orchestrator is part of the system under test here.
        services.AddSingleton(new CalluMetrics(new FakeMeterFactory()));
        services.AddScoped(_ => Substitute.For<IAuditLogService>());
        services.AddScoped<IEscalationOrchestrator, EscalationOrchestrator>();

        var provider = services.BuildServiceProvider();

        var persistence = new VoximplantVoiceCallbackPersistence(
            provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
            provider,
            NullLogger<VoximplantVoiceCallbackPersistence>.Instance);

        return (persistence, dispatcher, provider);
    }

    // escalationEverStarted says whether a run has ever begun; with escalationActive false it is what
    // tells "never happened" apart from "happened and is over".
    private static async Task<(Guid IncidentId, Guid PolicyId, Guid StepOneId, Guid StepTwoId)> SeedEscalatingIncidentAsync(
        string connectionString,
        bool escalationActive = true,
        DateTime? dispatchFailingSince = null,
        bool escalationEverStarted = true)
    {
        await using var db = PostgresFixture.Context(connectionString);

        db.Users.AddRange(
            new ApplicationUser { Id = "responder-1", UserName = "responder-1", Email = "responder-1@example.com" },
            new ApplicationUser { Id = "responder-2", UserName = "responder-2", Email = "responder-2@example.com" });

        var service = new Service { Name = "Payments" };
        var policy = new EscalationPolicy { Name = "Payments policy" };
        var stepOne = new EscalationStep
        {
            EscalationPolicy = policy,
            Level = 1,
            Title = "Primary",
            DelayMinutes = 0,
            TargetedUsers = { new EscalationStepUser { UserId = "responder-1" } }
        };
        var stepTwo = new EscalationStep
        {
            EscalationPolicy = policy,
            Level = 2,
            Title = "Secondary",
            DelayMinutes = 5,
            TargetedUsers = { new EscalationStepUser { UserId = "responder-2" } }
        };

        db.AddRange(service, policy, stepOne, stepTwo);
        await db.SaveChangesAsync();

        var incident = new Incident
        {
            Title = "Checkout failing",
            Description = "5xx on /pay",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.Open,
            StartedAt = DateTime.UtcNow,
            DataLanguage = "tr-TR",
            ServiceId = service.Id,
            EscalationPolicyId = policy.Id,
            IsEscalationActive = escalationActive,
            EscalationStartedAt = escalationEverStarted ? DateTime.UtcNow.AddMinutes(-10) : null,
            // Step 1 fired ten minutes ago: long enough that the sweep, which enforces step 2's own
            // 5-minute delay, is eligible to page step 2 the moment it is asked to.
            CurrentEscalationStepId = escalationActive ? stepOne.Id : null,
            LastEscalationStepAt = escalationActive ? DateTime.UtcNow.AddMinutes(-10) : null,
            DispatchFailingSince = dispatchFailingSince
        };

        db.Add(incident);
        await db.SaveChangesAsync();

        return (incident.Id, policy.Id, stepOne.Id, stepTwo.Id);
    }

    private sealed class CapturingDispatcher : INotificationDispatcher
    {
        public List<NotificationPayload> Payloads { get; } = [];

        /// <summary>What the notification store does when the next page is claimed. Null = success.</summary>
        public Func<NotificationDispatchResult>? Outcome { get; set; }

        private NotificationDispatchResult Record(NotificationPayload payload, int reached)
        {
            Payloads.Add(payload);
            return Outcome?.Invoke() ?? new NotificationDispatchResult(reached, 0);
        }

        public Task<NotificationDispatchResult> NotifyUsersAsync(
            IEnumerable<string> userIds, NotificationPayload payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record(payload, userIds.Count()));

        public Task<NotificationDispatchResult> NotifyOnCallAsync(
            Guid scheduleId, NotificationPayload payload, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record(payload, 1));

        public Task<NotificationDispatchResult> NotifyTeamAsync(
            Guid teamId, NotificationPayload payload, bool notifyAllMembers, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record(payload, 1));

        public Task ProcessNotificationQueueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<(bool Success, string Message)> SendTestNotificationAsync(
            string userId, string channel, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, string.Empty));
    }
}
