using Callu.Application.Services;
using Callu.Application.Validators;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Escalations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// An escalation ladder can be built and reordered through the real service against a real
/// PostgreSQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EscalationLadderTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task ThreeStepsAddedThroughTheService_LandAtDistinctAscendingLevels()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, scheduleId, teamId) = await SeedPolicyAsync(cs);

        await using var db = PostgresFixture.Context(cs);
        var service = Service(db);

        await service.AddEscalationStepAsync(policyId, Step("On-call engineer", 0, scheduleId: scheduleId));
        await service.AddEscalationStepAsync(policyId, Step("Team lead", 5, teamId: teamId));
        await service.AddEscalationStepAsync(policyId, Step("Whole team", 15, teamId: teamId, notifyAll: true));

        var levels = await LevelsAsync(cs, policyId);

        Assert.Equal(3, levels.Count);
        Assert.Equal(levels.Distinct().Count(), levels.Count);
        Assert.Equal(levels.Order().ToList(), levels);

        var titles = await TitlesInLevelOrderAsync(cs, policyId);
        Assert.Equal(new[] { "On-call engineer", "Team lead", "Whole team" }, titles);
    }

    [PostgresFact]
    public async Task AddingARungToAnExistingSingleStepPolicy_Succeeds()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, scheduleId, teamId) = await SeedPolicyAsync(cs);

        await using var db = PostgresFixture.Context(cs);
        var service = Service(db);

        await service.AddEscalationStepAsync(policyId, Step("On-call engineer", 0, scheduleId: scheduleId));
        await service.AddEscalationStepAsync(policyId, Step("Escalate to the team", 10, teamId: teamId));

        Assert.Equal(2, (await LevelsAsync(cs, policyId)).Distinct().Count());
    }

    [PostgresFact]
    public async Task ReorderingTwoSteps_PersistsTheNewOrder()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, _, _) = await SeedPolicyAsync(cs);

        var ids = await SeedStepsAsync(cs, policyId, "primary", "secondary", "everyone");

        await using var db = PostgresFixture.Context(cs);
        var service = Service(db);

        await service.ReorderStepsAsync(policyId, [ids[1], ids[0], ids[2]]);

        var titles = await TitlesInLevelOrderAsync(cs, policyId);
        Assert.Equal(new[] { "secondary", "primary", "everyone" }, titles);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // The premises the tests above rest on. Each of these survives the fix.

    [PostgresFact]
    public async Task TwoStepsAtTheSameLevel_AreRejectedByTheUniqueIndex()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, _, _) = await SeedPolicyAsync(cs);

        await using var db = PostgresFixture.Context(cs);

        db.EscalationSteps.Add(NewStep(policyId, level: 1, title: "first"));
        await db.SaveChangesAsync();

        db.EscalationSteps.Add(NewStep(policyId, level: 1, title: "second"));

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(thrown.InnerException).SqlState);
    }

    [PostgresFact]
    public async Task TheIndexIsPerPolicy_AndIgnoresSoftDeletedSteps()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyA, _, _) = await SeedPolicyAsync(cs);
        var (policyB, _, _) = await SeedPolicyAsync(cs);

        await using var db = PostgresFixture.Context(cs);

        var deleted = NewStep(policyA, level: 1, title: "retired");
        deleted.IsDeleted = true;

        db.EscalationSteps.AddRange(
            NewStep(policyA, level: 1, title: "policy A rung 1"),
            NewStep(policyB, level: 1, title: "policy B rung 1"),
            deleted);

        // No collision: different policies, and the third row is filtered out of the index.
        await db.SaveChangesAsync();

        Assert.Equal(3, await db.EscalationSteps.IgnoreQueryFilters().CountAsync());
    }

    [PostgresFact]
    public async Task TheServiceCanBeDriven_AndOneStepReallyLandsInPostgres()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, scheduleId, _) = await SeedPolicyAsync(cs);

        await using var db = PostgresFixture.Context(cs);
        var service = Service(db);

        var dto = await service.AddEscalationStepAsync(policyId, Step("On-call engineer", 0, scheduleId: scheduleId));

        Assert.Equal("On-call engineer", dto.Title);

        await using var verify = PostgresFixture.Context(cs);
        var stored = await verify.EscalationSteps.AsNoTracking().SingleAsync();
        Assert.Equal(policyId, stored.EscalationPolicyId);
        Assert.Equal("On-call engineer", stored.Title);
        Assert.Equal(scheduleId, stored.ScheduleId);
    }

    [PostgresFact]
    public async Task ReorderRejectsAnIncompleteStepList()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, _, _) = await SeedPolicyAsync(cs);
        var ids = await SeedStepsAsync(cs, policyId, "primary", "secondary");

        await using var db = PostgresFixture.Context(cs);
        var service = Service(db);

        // 422 with the ids that are missing or unknown, not a 500 that discards them. The ordinary
        // trigger is a caller whose cached list predates a concurrent step deletion.
        var ex = await Assert.ThrowsAsync<Callu.Shared.Exceptions.BusinessRuleException>(
            () => service.ReorderStepsAsync(policyId, [ids[0]]));

        Assert.Contains(ids[1].ToString(), ex.Message);
    }

    [PostgresFact]
    public async Task UpdatingOnlyTheDelay_LeavesThePagingTargetAlone()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, scheduleId, _) = await SeedPolicyAsync(cs);

        Guid stepId;
        await using (var db = PostgresFixture.Context(cs))
            stepId = (await Service(db).AddEscalationStepAsync(
                policyId, Step("On-call engineer", 0, scheduleId: scheduleId))).Id;

        await using (var db = PostgresFixture.Context(cs))
            Assert.True(await Service(db).UpdateStepAsync(
                policyId, stepId, new UpdateStepRequest { DelayMinutes = 10 }));

        await using var verify = PostgresFixture.Context(cs);
        var stored = await verify.EscalationSteps.AsNoTracking().SingleAsync();

        Assert.Equal(10, stored.DelayMinutes);
        Assert.Equal(scheduleId, stored.ScheduleId);
    }

    [PostgresFact]
    public async Task UpdatingTheTarget_StillReplacesIt()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, scheduleId, teamId) = await SeedPolicyAsync(cs);

        Guid stepId;
        await using (var db = PostgresFixture.Context(cs))
            stepId = (await Service(db).AddEscalationStepAsync(
                policyId, Step("On-call engineer", 0, scheduleId: scheduleId))).Id;

        // Exactly the body the web client sends: all three target fields, two of them cleared.
        await using (var db = PostgresFixture.Context(cs))
            await Service(db).UpdateStepAsync(policyId, stepId, new UpdateStepRequest
            {
                ScheduleId = null,
                TeamId = teamId,
                NotifyUserIds = []
            });

        await using var verify = PostgresFixture.Context(cs);
        var stored = await verify.EscalationSteps.AsNoTracking().SingleAsync();

        Assert.Null(stored.ScheduleId);
        Assert.Equal(teamId, stored.TeamId);
    }

    /// <summary>The ladder the operator reads has to be the ladder the orchestrator walks.</summary>
    // Level 2 is written first on purpose: that is the row order a small table hands back, and it is
    // what made a two-step policy render backwards on the list screen.
    [PostgresFact]
    public async Task StepsComeBackByLevel_FromEveryReadThatShowsThem()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, scheduleId, teamId) = await SeedPolicyAsync(cs);

        await using (var seed = PostgresFixture.Context(cs))
        {
            seed.Add(new EscalationStep
            {
                EscalationPolicyId = policyId,
                Level = 2,
                Title = "Team lead",
                DelayMinutes = 15,
                TeamId = teamId
            });
            await seed.SaveChangesAsync();

            seed.Add(new EscalationStep
            {
                EscalationPolicyId = policyId,
                Level = 1,
                Title = "Primary on-call",
                DelayMinutes = 0,
                ScheduleId = scheduleId
            });
            await seed.SaveChangesAsync();
        }

        await using var db = PostgresFixture.Context(cs);
        var service = Service(db);

        var detail = await service.GetEscalationPolicyByIdAsync(policyId);
        Assert.NotNull(detail);
        Assert.Equal([1, 2], detail!.Steps.Select(s => s.Level).ToArray());

        var listed = (await service.GetEscalationPoliciesAsync()).Single(p => p.Id == policyId);
        Assert.Equal([1, 2], listed.Steps.Select(s => s.Level).ToArray());

        var byTeam = (await service.GetEscalationPoliciesByTeamAsync(teamId)).Single(p => p.Id == policyId);
        Assert.Equal([1, 2], byTeam.Steps.Select(s => s.Level).ToArray());
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Harness.

    // Real repositories, TransactionManager and validators; only UserManager is substituted.
    private static IEscalationService Service(ApplicationDbContext db)
    {
        var userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        return new EscalationService(
            new EscalationPolicyRepository(db, NullLogger<EscalationPolicyRepository>.Instance),
            new EscalationStepRepository(db, NullLogger<EscalationStepRepository>.Instance),
            new TransactionManager(db, NullLogger<TransactionManager>.Instance),
            userManager,
            new CreateEscalationRequestValidator(),
            new CreateEscalationStepRequestValidator(),
            Substitute.For<IAuditLogService>(),
            NullLogger<EscalationService>.Instance);
    }

    // No Level, because the web UI's step-mapping.ts does not send one: the service derives it.
    private static CreateEscalationStepRequest Step(
        string title, int delayMinutes, Guid? scheduleId = null, Guid? teamId = null, bool notifyAll = false) =>
        new()
        {
            Title = title,
            DelayMinutes = delayMinutes,
            ScheduleId = scheduleId,
            TeamId = teamId,
            NotifyAllTeamMembers = notifyAll
        };

    private static EscalationStep NewStep(Guid policyId, int level, string title) => new()
    {
        Id = Guid.NewGuid(),
        EscalationPolicyId = policyId,
        Level = level,
        Title = title,
        DelayMinutes = level * 5,
        CreatedAt = DateTime.UtcNow
    };

    private static async Task<(Guid PolicyId, Guid ScheduleId, Guid TeamId)> SeedPolicyAsync(string connectionString)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var team = new Team { Id = Guid.NewGuid(), Name = $"Payments {Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
        var schedule = new Schedule
        {
            Id = Guid.NewGuid(),
            Name = $"Primary {Guid.NewGuid():N}",
            Timezone = "Europe/Istanbul",
            TeamId = team.Id,
            CreatedAt = DateTime.UtcNow
        };
        var policy = new EscalationPolicy
        {
            Id = Guid.NewGuid(),
            Name = $"Payments paging {Guid.NewGuid():N}",
            TeamId = team.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Teams.Add(team);
        db.Schedules.Add(schedule);
        db.EscalationPolicies.Add(policy);
        await db.SaveChangesAsync();

        return (policy.Id, schedule.Id, team.Id);
    }

    // Seeded directly at levels 1..n, past AddEscalationStepAsync, so a reorder test fails for the
    // reorder's own reason rather than for how a level is derived on add.
    private static async Task<List<Guid>> SeedStepsAsync(string connectionString, Guid policyId, params string[] titles)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var steps = titles.Select((title, i) => NewStep(policyId, level: i + 1, title: title)).ToList();
        db.EscalationSteps.AddRange(steps);
        await db.SaveChangesAsync();

        return steps.Select(s => s.Id).ToList();
    }

    private static async Task<List<int>> LevelsAsync(string connectionString, Guid policyId)
    {
        await using var db = PostgresFixture.Context(connectionString);

        return await db.EscalationSteps.AsNoTracking()
            .Where(s => s.EscalationPolicyId == policyId && !s.IsDeleted)
            .OrderBy(s => s.CreatedAt)
            .Select(s => s.Level)
            .ToListAsync();
    }

    private static async Task<List<string>> TitlesInLevelOrderAsync(string connectionString, Guid policyId)
    {
        await using var db = PostgresFixture.Context(connectionString);

        return await db.EscalationSteps.AsNoTracking()
            .Where(s => s.EscalationPolicyId == policyId && !s.IsDeleted)
            .OrderBy(s => s.Level)
            .Select(s => s.Title)
            .ToListAsync();
    }
}
