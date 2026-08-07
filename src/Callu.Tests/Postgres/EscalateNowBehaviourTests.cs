using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Behaviour of <see cref="IEscalationOrchestrator.EscalateNowAsync"/> — a responder pressing 2 on the phone.</summary>
// The one rule: pressing 2 must never, by itself, cause the caller to be paged again.
[Collection(PostgresCollection.Name)]
public class EscalateNowBehaviourTests(PostgresFixture pg)
{
    // ---------------------------------------------------------------- the caller is never re-paged

    /// <summary>A one-step policy is exhausted the moment it has paged, so pressing 2 must not restart it.</summary>
    [PostgresFact]
    public async Task PressTwo_OnAnExhaustedSingleStepPolicy_DoesNotRingTheCallerBack()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s => s.Steps = 1);
        await using var world = World(cs);

        var escalated = await world.Orchestrator.EscalateNowAsync(seed.IncidentId);

        Assert.False(escalated);
        Assert.Empty(world.Dispatcher.Pages);

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        // The run is over and stays over — the pointer is not rewound and no new run is started. The
        // dedupe generation is the tell: a new run would have stamped a new EscalationStartedAt, which
        // is exactly what stopped the re-page from being suppressed as a duplicate.
        Assert.Equal(seed.StepIds[0], incident.CurrentEscalationStepId);
        Assert.False(incident.IsEscalationActive);
        Assert.Equal(
            EscalationOrchestrator.ComputeDispatchGeneration(seed.EscalationStartedAt),
            EscalationOrchestrator.ComputeDispatchGeneration(incident.EscalationStartedAt));

        // ...and the responder's request is not silently dropped: the operator can see that the
        // keypress paged nobody and that the policy is the reason.
        Assert.True(await db.Set<IncidentTimelineEvent>().AnyAsync(e =>
            e.IncidentId == seed.IncidentId && e.Title.Contains("the policy is exhausted")));
        await world.Audit.Received().LogAsync(
            "system:escalation", AuditAction.EscalationNobodyReached, "Incident", seed.IncidentId.ToString(),
            Arg.Any<string?>(), Arg.Is<string?>(d => d != null && d.Contains("exhausted")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Pressing 2 on an exhausted multi-step policy pages nobody and records "exhausted" once.</summary>
    [PostgresFact]
    public async Task PressTwo_OnAnExhaustedPolicy_DoesNotRerunTheChain_NorRepeatTheExhaustedRecord()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        // Pointer on the LAST step: the policy has paged everyone it has.
        var seed = await SeedAsync(cs, s => s.PointerStepIndex = 1);
        await using var world = World(cs);

        Assert.False(await world.Orchestrator.EscalateNowAsync(seed.IncidentId));
        Assert.False(await world.Orchestrator.EscalateNowAsync(seed.IncidentId));

        // The sweep gets its turn too: a restarted run would have left the incident escalating and the
        // sweep would have paged step 1, then step 2, all over again.
        await world.Orchestrator.ProcessPendingEscalationsAsync();

        Assert.Empty(world.Dispatcher.Pages);

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);
        Assert.Equal(seed.StepIds[1], incident.CurrentEscalationStepId);
        Assert.False(incident.IsEscalationActive);

        var exhaustedEvents = await db.Set<IncidentTimelineEvent>()
            .CountAsync(e => e.IncidentId == seed.IncidentId && e.Title == "Escalation exhausted");
        Assert.Equal(1, exhaustedEvents);
    }

    /// <summary>Between two repeat passes the pointer is null by design, and pressing 2 must not restart the ladder.</summary>
    [PostgresFact]
    public async Task PressTwo_BetweenRepeatCycles_DoesNotRestartTheLadder()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s => s.BetweenRepeatCycles = true);
        await using var world = World(cs);

        Assert.False(await world.Orchestrator.EscalateNowAsync(seed.IncidentId));
        Assert.Empty(world.Dispatcher.Pages);

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        // The pass the policy still owes is left exactly as the sweep set it up: the keypress neither
        // consumed the new cycle's step-1 slot nor re-stamped the clock the sweep paces itself by.
        Assert.Null(incident.CurrentEscalationStepId);
        Assert.True(incident.IsEscalationActive);
        Assert.Equal(1, incident.EscalationCyclesCompleted);
        Assert.True(incident.LastEscalationStepAt < DateTime.UtcNow.AddMinutes(-10));

        Assert.True(await db.Set<IncidentTimelineEvent>().AnyAsync(e =>
            e.IncidentId == seed.IncidentId && e.Title.Contains("between passes")));
        await world.Audit.Received().LogAsync(
            "system:escalation", AuditAction.EscalationNobodyReached, "Incident", seed.IncidentId.ToString(),
            Arg.Any<string?>(), Arg.Is<string?>(d => d != null && d.Contains("between repeat passes")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------- what it SHOULD do

    /// <summary>The case the feature exists for: exactly one step moves, and the caller is not on it.</summary>
    [PostgresFact]
    public async Task PressTwo_OnARunningEscalation_PagesTheNextStep_AndOnlyIt()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs);
        await using var world = World(cs);

        Assert.True(await world.Orchestrator.EscalateNowAsync(seed.IncidentId));

        var page = Assert.Single(world.Dispatcher.Pages);
        Assert.Equal(2, page.Payload.EscalationLevel);
        Assert.Equal(["responder-2"], page.UserIds);

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        Assert.Equal(seed.StepIds[1], incident.CurrentEscalationStepId);
        // Still Open, so the sweep keeps the chain — the keypress accelerated it, it did not end it.
        Assert.True(incident.IsEscalationActive);
        Assert.Null(incident.DispatchFailingSince);
    }

    /// <summary>Both paths resolve a step's target through the same precedence: users beat a schedule beats a team.</summary>
    [PostgresFact]
    public async Task PressTwo_ResolvesTheStepsTarget_TheSameWayTheSweepDoes()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s => s.SecondStepAlsoNamesASchedule = true);
        await using var world = World(cs);

        Assert.True(await world.Orchestrator.EscalateNowAsync(seed.IncidentId));

        var page = Assert.Single(world.Dispatcher.Pages);
        Assert.Equal(["responder-2"], page.UserIds);
        Assert.Empty(world.Dispatcher.SchedulesPaged);
    }

    // ---------------------------------------------------------------- no zombie escalations

    /// <summary>Pressing 2 on an acknowledged incident is a one-shot page that leaves escalation closed.</summary>
    // The sweep skips Acknowledged but not Investigating, so a leftover active flag would page the whole policy.
    [PostgresFact]
    public async Task PressTwo_OnAnAcknowledgedIncident_PagesOnce_AndDoesNotLeaveAZombieEscalation()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        // Three steps, acknowledged by a human, pointer on step 1 (Incident.Acknowledge() leaves it).
        var seed = await SeedAsync(cs, s =>
        {
            s.Steps = 3;
            s.Status = IncidentStatus.Acknowledged;
            s.EscalationActive = false;
        });
        await using var world = World(cs);

        Assert.True(await world.Orchestrator.EscalateNowAsync(seed.IncidentId));

        var page = Assert.Single(world.Dispatcher.Pages);
        Assert.Equal(2, page.Payload.EscalationLevel);

        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

            // The step that went out is recorded — the timeline does not claim nothing happened...
            Assert.Equal(seed.StepIds[1], incident.CurrentEscalationStepId);
            Assert.True(await db.Set<IncidentTimelineEvent>().AnyAsync(e =>
                e.IncidentId == seed.IncidentId && e.Title.Contains("Escalation step 2 triggered")));

            // ...but escalation is closed. Nothing follows this one step.
            Assert.False(incident.IsEscalationActive);
        }

        // The human takes the next normal step with the incident they own. Nobody gets paged for it.
        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = await db.Incidents.FirstAsync(i => i.Id == seed.IncidentId);
            incident.StartInvestigation("the-human");
            await db.SaveChangesAsync();
        }

        await world.Orchestrator.ProcessPendingEscalationsAsync();

        Assert.Single(world.Dispatcher.Pages);
    }

    /// <summary>An ack landing mid-dispatch wins: the pointer stays put and escalation is not left running.</summary>
    // The commit loses the xmin race rather than standing down, so the step is recorded as not applied.
    [PostgresFact]
    public async Task PressTwo_WhenAnAckLandsMidDispatch_LeavesTheAckStanding_AndNoZombieEscalation()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s => s.Steps = 3);
        await using var world = World(cs);

        // The human acknowledges while the page for step 2 is being dispatched.
        world.Dispatcher.OnDispatch = async () =>
        {
            await using var db = PostgresFixture.Context(cs);
            var incident = await db.Incidents.FirstAsync(i => i.Id == seed.IncidentId);
            incident.Acknowledge("the-human");
            await db.SaveChangesAsync();
        };

        Assert.False(await world.Orchestrator.EscalateNowAsync(seed.IncidentId));

        // The page did go out — the dispatch happens before the commit, on purpose.
        Assert.Single(world.Dispatcher.Pages);

        await using (var db = PostgresFixture.Context(cs))
        {
            var after = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

            Assert.Equal(IncidentStatus.Acknowledged, after.Status);
            Assert.Equal(seed.StepIds[0], after.CurrentEscalationStepId);
            Assert.False(after.IsEscalationActive);

            Assert.True(await db.Set<IncidentTimelineEvent>().AnyAsync(e =>
                e.IncidentId == seed.IncidentId && e.Title.Contains("was not applied")));
        }

        // And nothing follows: the human owns the incident, and the sweep does not page the rest of the
        // policy at them.
        world.Dispatcher.OnDispatch = null;
        await world.Orchestrator.ProcessPendingEscalationsAsync();
        Assert.Single(world.Dispatcher.Pages);
    }

    // ---------------------------------------------------------------- honesty when it goes wrong

    /// <summary>A keypress has no next tick, so a lost race is recorded rather than thrown.</summary>
    [PostgresFact]
    public async Task PressTwo_ThatLosesAConcurrencyRace_SaysSo_InsteadOfThrowing()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs);

        // Call 1 is the plan, call 2 is the commit of the step that was just dispatched.
        await using var world = World(cs, throwConcurrencyOnTransaction: 2);

        var escalated = await world.Orchestrator.EscalateNowAsync(seed.IncidentId);

        Assert.False(escalated);

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == seed.IncidentId);

        // The step did not advance...
        Assert.Equal(seed.StepIds[0], incident.CurrentEscalationStepId);

        // ...and the operator is told, rather than the drop living only in a log line that lied. The
        // record does not claim nobody was paged: this race was lost AFTER the dispatch, so a page for
        // the step did go out — what was lost is the record of it.
        Assert.True(await db.Set<IncidentTimelineEvent>().AnyAsync(e =>
            e.IncidentId == seed.IncidentId && e.Title.Contains("was not applied")));
    }

    /// <summary>Pressing 2 on a resolved incident pages nobody, and the timeline says so.</summary>
    [PostgresFact]
    public async Task PressTwo_OnAResolvedIncident_PagesNobody_AndSaysSo()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s => s.Status = IncidentStatus.Resolved);
        await using var world = World(cs);

        Assert.False(await world.Orchestrator.EscalateNowAsync(seed.IncidentId));
        Assert.Empty(world.Dispatcher.Pages);

        await using var db = PostgresFixture.Context(cs);
        Assert.True(await db.Set<IncidentTimelineEvent>().AnyAsync(e =>
            e.IncidentId == seed.IncidentId && e.Title.Contains("already closed")));
    }

    // ---------------------------------------------------------------- harness

    private sealed class SeedOptions
    {
        public int Steps { get; set; } = 2;
        public int PointerStepIndex { get; set; }
        public bool EscalationActive { get; set; } = true;
        public IncidentStatus Status { get; set; } = IncidentStatus.Open;
        public bool SecondStepAlsoNamesASchedule { get; set; }

        /// <summary>Seeds the state a finished Repeat pass leaves: active, one cycle done, no pointer.</summary>
        public bool BetweenRepeatCycles { get; set; }
    }

    private sealed record Seed(Guid IncidentId, Guid PolicyId, Guid[] StepIds, DateTime EscalationStartedAt);

    private static async Task<Seed> SeedAsync(string connectionString, Action<SeedOptions>? configure = null)
    {
        var options = new SeedOptions();
        configure?.Invoke(options);

        await using var db = PostgresFixture.Context(connectionString);

        for (var i = 1; i <= options.Steps; i++)
            db.Users.Add(new ApplicationUser
            {
                Id = $"responder-{i}",
                UserName = $"responder-{i}",
                Email = $"responder-{i}@example.com"
            });

        var team = new Team { Name = "Payments team" };
        var schedule = new Schedule { Name = "Primary rota", Team = team, Timezone = "UTC" };
        var policy = new EscalationPolicy { Name = "Payments policy" };

        if (options.BetweenRepeatCycles)
        {
            policy.ExhaustionBehavior = EscalationExhaustionBehavior.Repeat;
            policy.MaxRepeatCycles = 3;
            policy.MaxRepeatDurationMinutes = 240;
        }

        var steps = new List<EscalationStep>();
        for (var i = 1; i <= options.Steps; i++)
        {
            var step = new EscalationStep
            {
                EscalationPolicy = policy,
                Level = i,
                Title = $"Step {i}",
                DelayMinutes = i == 1 ? 0 : 5,
                TargetedUsers = { new EscalationStepUser { UserId = $"responder-{i}" } }
            };

            // A step that names BOTH users and a schedule: ResolveTarget must pick the users, on every
            // path that pages a step.
            if (i == 2 && options.SecondStepAlsoNamesASchedule)
                step.Schedule = schedule;

            steps.Add(step);
        }

        db.AddRange(team, schedule, policy);
        db.AddRange(steps);
        await db.SaveChangesAsync();

        var startedAt = DateTime.UtcNow.AddMinutes(-30);
        var incident = new Incident
        {
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = options.Status,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            TeamId = team.Id,
            EscalationPolicyId = policy.Id,
            IsEscalationActive = options.EscalationActive && options.Status == IncidentStatus.Open,
            EscalationStartedAt = startedAt,
            CurrentEscalationStepId = options.BetweenRepeatCycles ? null : steps[options.PointerStepIndex].Id,
            EscalationCyclesCompleted = options.BetweenRepeatCycles ? 1 : 0,
            // Long enough ago that the sweep would be eligible to page the next step: the tests that
            // assert "the sweep pages nobody" have to mean it, not just be early.
            LastEscalationStepAt = DateTime.UtcNow.AddMinutes(-20),
            AcknowledgedAt = options.Status == IncidentStatus.Acknowledged ? DateTime.UtcNow : null,
            AcknowledgedBy = options.Status == IncidentStatus.Acknowledged ? "the-human" : null,
            ResolvedAt = options.Status == IncidentStatus.Resolved ? DateTime.UtcNow : null,
            ResolvedBy = options.Status == IncidentStatus.Resolved ? "the-human" : null
        };

        db.Add(incident);
        await db.SaveChangesAsync();

        return new Seed(incident.Id, policy.Id, steps.Select(s => s.Id).ToArray(), incident.EscalationStartedAt!.Value);
    }

    private sealed class TestWorld : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IServiceScope Scope { get; init; }
        public required IEscalationOrchestrator Orchestrator { get; init; }
        public required RecordingDispatcher Dispatcher { get; init; }
        public required IAuditLogService Audit { get; init; }

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    // throwConcurrencyOnTransaction is the 1-based index of the transaction that loses the race, 0 for none.
    private static TestWorld World(string connectionString, int throwConcurrencyOnTransaction = 0)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);

        var dispatcher = new RecordingDispatcher();
        var audit = Substitute.For<IAuditLogService>();
        services.AddSingleton<INotificationDispatcher>(dispatcher);
        services.AddSingleton(audit);
        services.AddSingleton(new CalluMetrics(new FakeMeterFactory()));

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        ITransactionManager transactions = sp.GetRequiredService<ITransactionManager>();
        if (throwConcurrencyOnTransaction > 0)
            transactions = new RaceLosingTransactionManager(transactions, throwConcurrencyOnTransaction);

        var orchestrator = new EscalationOrchestrator(
            sp.GetRequiredService<IIncidentRepository>(),
            sp.GetRequiredService<IEscalationPolicyRepository>(),
            sp.GetRequiredService<IIncidentTimelineEventRepository>(),
            sp.GetRequiredService<ICallLogRepository>(),
            audit,
            transactions,
            dispatcher,
            sp.GetRequiredService<CalluMetrics>(),
            NullLogger<EscalationOrchestrator>.Instance);

        return new TestWorld
        {
            Provider = provider,
            Scope = scope,
            Orchestrator = orchestrator,
            Dispatcher = dispatcher,
            Audit = audit
        };
    }

    private sealed record Page(NotificationPayload Payload, string[] UserIds);

    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        public List<Page> Pages { get; } = [];
        public List<Guid> SchedulesPaged { get; } = [];

        /// <summary>Runs while the page is "in flight" — lets a test land an ack mid-dispatch.</summary>
        public Func<Task>? OnDispatch { get; set; }

        public async Task<NotificationDispatchResult> NotifyUsersAsync(
            IEnumerable<string> userIds, NotificationPayload payload, CancellationToken cancellationToken = default)
        {
            var ids = userIds.ToArray();
            Pages.Add(new Page(payload, ids));
            if (OnDispatch is not null) await OnDispatch();
            return new NotificationDispatchResult(ids.Length, 0);
        }

        public async Task<NotificationDispatchResult> NotifyOnCallAsync(
            Guid scheduleId, NotificationPayload payload, CancellationToken cancellationToken = default)
        {
            SchedulesPaged.Add(scheduleId);
            Pages.Add(new Page(payload, []));
            if (OnDispatch is not null) await OnDispatch();
            return new NotificationDispatchResult(1, 0);
        }

        public async Task<NotificationDispatchResult> NotifyTeamAsync(
            Guid teamId, NotificationPayload payload, bool notifyAllMembers, CancellationToken cancellationToken = default)
        {
            Pages.Add(new Page(payload, []));
            if (OnDispatch is not null) await OnDispatch();
            return new NotificationDispatchResult(1, 0);
        }

        public Task ProcessNotificationQueueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<(bool Success, string Message)> SendTestNotificationAsync(
            string userId, string channel, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, string.Empty));
    }

    private sealed class RaceLosingTransactionManager(ITransactionManager inner, int throwOnCall) : ITransactionManager
    {
        private int _calls;

        private void Count()
        {
            if (Interlocked.Increment(ref _calls) == throwOnCall)
                throw new DbUpdateConcurrencyException(
                    "simulated: another writer changed this incident between the dispatch and the commit");
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
        {
            Count();
            return inner.ExecuteInTransactionAsync(operation, cancellationToken);
        }

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            Count();
            return inner.ExecuteInTransactionAsync(operation, cancellationToken);
        }

        public bool IsInTransaction() => inner.IsInTransaction();
    }
}
