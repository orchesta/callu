using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Messaging;
using Callu.Application.Plugins;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Infrastructure.Utilities;
using Callu.Shared.Models.Incidents;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>What <c>GET /incidents/{id}/escalation</c> reports, against a real database.</summary>
// The value it carries is a countdown to the next page. Computing that anywhere but next to the
// orchestrator's own rules puts a second copy of them in the product, and the two drift.
[Collection(PostgresCollection.Name)]
public class IncidentEscalationReportTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task AnIncidentThatDoesNotExist_ReportsNothing()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var world = World(cs);

        Assert.Null(await world.Incidents.GetEscalationAsync(Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task AnIncidentWithNoPolicy_SaysSo_RatherThanReportingAnEmptyLadder()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var seed = await SeedAsync(cs, s => s.AttachPolicy = false);

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.Equal(IncidentEscalationRunState.NotConfigured, report.RunState);
        Assert.Empty(report.Steps);
        Assert.Null(report.NextStepDueAt);
    }

    /// <summary>The first step is not spaced from an earlier page, so its own delay is the whole wait.</summary>
    [PostgresFact]
    public async Task BeforeAnyStepHasPaged_TheFirstStepIsDueAtTheRunsStartPlusItsOwnDelay()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        // Running, but the pointer is still null: nothing has paged yet.
        var seed = await SeedAsync(cs, s =>
        {
            s.PointerStepIndex = null;
            s.FirstStepDelayMinutes = 0;
        });

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.Equal(IncidentEscalationRunState.Running, report.RunState);
        Assert.Null(report.CurrentStepId);
        // Read back rather than compared against the seed's own value: Postgres stores microseconds,
        // so the round-tripped instant is the only one the report could have returned.
        Assert.Equal(await StoredEscalationStartAsync(cs, seed.IncidentId), report.NextStepDueAt);
        Assert.All(report.Steps, s => Assert.Equal(IncidentEscalationStepState.Pending, s.State));
    }

    /// <summary>Between two pages the gap is floored, so a 0-minute later step still waits.</summary>
    [PostgresFact]
    public async Task AfterAStepHasPaged_TheNextIsDueAtTheFlooredGap_NotItsWrittenDelay()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var lastStepAt = DateTime.UtcNow.AddSeconds(-30);
        var seed = await SeedAsync(cs, s =>
        {
            s.PointerStepIndex = 0;
            s.LastEscalationStepAt = lastStepAt;
            s.SecondStepDelayMinutes = 0;
        });

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        var stored = await StoredLastStepAtAsync(cs, seed.IncidentId);
        Assert.Equal(
            stored!.Value.AddMinutes(EscalationCalculations.MinDelayMinutesBetweenSteps),
            report.NextStepDueAt);

        // The written delay was 0, so an unfloored gap would have made it due the moment the last
        // step paged.
        Assert.NotEqual(stored, report.NextStepDueAt);
    }

    private static async Task<DateTime?> StoredEscalationStartAsync(string connectionString, Guid incidentId)
    {
        await using var db = PostgresFixture.Context(connectionString);
        return await db.Incidents.AsNoTracking()
            .Where(i => i.Id == incidentId)
            .Select(i => i.EscalationStartedAt)
            .FirstAsync();
    }

    private static async Task<DateTime?> StoredLastStepAtAsync(string connectionString, Guid incidentId)
    {
        await using var db = PostgresFixture.Context(connectionString);
        return await db.Incidents.AsNoTracking()
            .Where(i => i.Id == incidentId)
            .Select(i => i.LastEscalationStepAt)
            .FirstAsync();
    }

    /// <summary>A step that reached nobody backdates the clock so the next one goes on the following tick.</summary>
    // Reported as written: a countdown here would show time remaining for a page already on its way.
    [PostgresFact]
    public async Task WhenTheClockWasBackdatedBecauseAStepReachedNobody_TheNextStepReadsAsAlreadyDue()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var backdated = DateTime.UtcNow.AddMinutes(-(EscalationCalculations.MinDelayMinutesBetweenSteps + 1));
        var seed = await SeedAsync(cs, s =>
        {
            s.PointerStepIndex = 0;
            s.LastEscalationStepAt = backdated;
            s.SecondStepDelayMinutes = 0;
        });

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.NotNull(report.NextStepDueAt);
        Assert.True(report.NextStepDueAt < DateTime.UtcNow,
            "a backdated step clock means the next page is already due, not pending");
    }

    /// <summary>A call acknowledged on the phone clears the step pointer on purpose.</summary>
    // Deriving "never started" from that pointer labels the incident that certainly did escalate
    // as the one that never did, which is what the screen showed on the first real incident.
    [PostgresFact]
    public async Task AnIncidentWhoseStepPointerWasClearedOnAcknowledgement_IsNotReportedAsNeverStarted()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s =>
        {
            s.Status = IncidentStatus.Acknowledged;
            s.PointerStepIndex = null;
        });

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.NotEqual(IncidentEscalationRunState.Waiting, report.RunState);
        Assert.Equal(IncidentEscalationRunState.Stopped, report.RunState);
    }

    /// <summary>Only an escalation that never began reads as waiting.</summary>
    [PostgresFact]
    public async Task AnIncidentWhoseRunNeverBegan_ReportsWaiting()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s =>
        {
            s.PointerStepIndex = null;
            s.EscalationActive = false;
            s.NeverStarted = true;
        });

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.Equal(IncidentEscalationRunState.Waiting, report.RunState);
    }

    /// <summary>With the pointer gone, a step the timeline says paged is still behind us.</summary>
    [PostgresFact]
    public async Task WithNoPointer_AStepTheTimelineRecordsAsPaged_ReadsAsPassed()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s =>
        {
            s.Status = IncidentStatus.Acknowledged;
            s.PointerStepIndex = null;
        });

        await using (var db = PostgresFixture.Context(cs))
        {
            db.Add(new IncidentTimelineEvent
            {
                IncidentId = seed.IncidentId,
                EventType = TimelineEventType.Escalated,
                EscalationStepId = seed.StepIds[0],
                Title = "Escalation step 1 triggered",
                ActorUserId = "system",
            });
            await db.SaveChangesAsync();
        }

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.Equal(IncidentEscalationStepState.Passed, report.Steps[0].State);
        Assert.Equal(IncidentEscalationStepState.Pending, report.Steps[1].State);
    }

    [PostgresFact]
    public async Task AnAcknowledgedIncident_ReportsStopped_AndCountsDownToNothing()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s =>
        {
            s.Status = IncidentStatus.Acknowledged;
            s.PointerStepIndex = 0;
        });

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.Equal(IncidentEscalationRunState.Stopped, report.RunState);
        Assert.Null(report.NextStepDueAt);
    }

    /// <summary>The state the screen exists to make visible: the policy ran out and nobody has the incident.</summary>
    [PostgresFact]
    public async Task AnOpenIncidentPastItsLastStep_ReportsExhausted()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s =>
        {
            s.PointerStepIndex = 1;
            s.EscalationActive = false;
        });

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.Equal(IncidentEscalationRunState.Exhausted, report.RunState);
        Assert.Null(report.NextStepDueAt);
        Assert.Equal(seed.StepIds[1], report.CurrentStepId);
    }

    [PostgresFact]
    public async Task EachStepCarriesWhereTheRunStands_PassedThenCurrentThenPending()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s =>
        {
            s.Steps = 3;
            s.PointerStepIndex = 1;
        });

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.Equal(
            new[]
            {
                IncidentEscalationStepState.Passed,
                IncidentEscalationStepState.Current,
                IncidentEscalationStepState.Pending,
            },
            report.Steps.Select(s => s.State));
    }

    /// <summary>When a step paged is read from the timeline row that step wrote, not from a parsed title.</summary>
    [PostgresFact]
    public async Task AStepThatPaged_CarriesTheInstantItPaged()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s => s.PointerStepIndex = 0);

        await using (var db = PostgresFixture.Context(cs))
        {
            db.Add(new IncidentTimelineEvent
            {
                IncidentId = seed.IncidentId,
                EventType = TimelineEventType.Escalated,
                EscalationStepId = seed.StepIds[0],
                Title = "Escalation step 1 triggered",
                ActorUserId = "system",
            });
            await db.SaveChangesAsync();
        }

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.NotNull(report.Steps[0].PagedAt);
        // The step that has not run yet carries nothing, rather than borrowing its neighbour's time.
        Assert.Null(report.Steps[1].PagedAt);
    }

    /// <summary>Steps come back in the order the orchestrator picks the next one in.</summary>
    // Ordering them any other way names a step other than the one about to page.
    [PostgresFact]
    public async Task StepsAreReportedInLevelOrder()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs, s => s.Steps = 3);

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        Assert.Equal([1, 2, 3], report.Steps.Select(s => s.Level));
    }

    [PostgresFact]
    public async Task AStepTargetingUsers_CarriesTheirNames_NotTheirIds()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var seed = await SeedAsync(cs);

        await using var world = World(cs);
        var report = await world.Incidents.GetEscalationAsync(seed.IncidentId);

        Assert.NotNull(report);
        var names = Assert.Single(report.Steps[0].NotifyUserNames);
        Assert.DoesNotContain("responder-1", names, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- harness

    private sealed class SeedOptions
    {
        public int Steps { get; set; } = 2;

        /// <summary>Null means the run is under way but no step has paged yet.</summary>
        public int? PointerStepIndex { get; set; } = 0;

        public bool AttachPolicy { get; set; } = true;
        public bool EscalationActive { get; set; } = true;
        public IncidentStatus Status { get; set; } = IncidentStatus.Open;
        public int FirstStepDelayMinutes { get; set; }
        public int SecondStepDelayMinutes { get; set; } = 5;
        public DateTime? LastEscalationStepAt { get; set; }

        /// <summary>No run was ever triggered, so there is no start instant.</summary>
        public bool NeverStarted { get; set; }
    }

    private sealed record Seed(Guid IncidentId, Guid[] StepIds, DateTime EscalationStartedAt);

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
                Email = $"responder-{i}@example.com",
                FirstName = "Ada",
                LastName = $"Number{i}",
            });

        var team = new Team { Name = "Payments team" };
        var policy = new EscalationPolicy { Name = "Payments policy" };

        var steps = new List<EscalationStep>();
        for (var i = 1; i <= options.Steps; i++)
        {
            steps.Add(new EscalationStep
            {
                EscalationPolicy = policy,
                Level = i,
                Title = $"Step {i}",
                DelayMinutes = i == 1 ? options.FirstStepDelayMinutes : options.SecondStepDelayMinutes,
                TargetedUsers = { new EscalationStepUser { UserId = $"responder-{i}" } },
            });
        }

        db.AddRange(team, policy);
        db.AddRange(steps);
        await db.SaveChangesAsync();

        var startedAt = DateTime.UtcNow.AddMinutes(-30);
        var incident = new Incident
        {
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = options.Status,
            StartedAt = startedAt,
            TeamId = team.Id,
            EscalationPolicyId = options.AttachPolicy ? policy.Id : null,
            IsEscalationActive = options.AttachPolicy
                                 && options.EscalationActive
                                 && options.Status == IncidentStatus.Open,
            EscalationStartedAt = options.AttachPolicy && !options.NeverStarted ? startedAt : null,
            CurrentEscalationStepId = options.AttachPolicy && options.PointerStepIndex is int index
                ? steps[index].Id
                : null,
            LastEscalationStepAt = options.LastEscalationStepAt,
            AcknowledgedAt = options.Status == IncidentStatus.Acknowledged ? DateTime.UtcNow : null,
            AcknowledgedBy = options.Status == IncidentStatus.Acknowledged ? "the-human" : null,
        };

        db.Add(incident);
        await db.SaveChangesAsync();

        return new Seed(incident.Id, steps.Select(s => s.Id).ToArray(), startedAt);
    }

    private sealed class TestWorld : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IServiceScope Scope { get; init; }
        public required IIncidentService Incidents { get; init; }

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private static TestWorld World(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(connectionString, enableParameterLogging: false);
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var incidents = new IncidentService(
            sp.GetRequiredService<IIncidentRepository>(),
            sp.GetRequiredService<IIncidentTimelineEventRepository>(),
            sp.GetRequiredService<IEscalationPolicyRepository>(),
            Substitute.For<ITeamMemberRepository>(),
            Substitute.For<ICallLogRepository>(),
            Substitute.For<IRepository<WebhookDelivery>>(),
            Substitute.For<IRepository<ConferenceRoom>>(),
            Substitute.For<ITransactionManager>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            Substitute.For<IValidator<CreateIncidentRequest>>(),
            Substitute.For<IIncidentEventDispatcher>(),
            Substitute.For<IEscalationOrchestrator>(),
            Substitute.For<IEscalationWorkflowSignal>(),
            Substitute.For<IAlertRuleEngine>(),
            Substitute.For<IAuditLogService>(),
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IServiceRepository>(),
            Substitute.For<INotificationChannelService>(),
            Substitute.For<IMaintenanceWindowService>(),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<IncidentService>.Instance);

        return new TestWorld { Provider = provider, Scope = scope, Incidents = incidents };
    }
}
