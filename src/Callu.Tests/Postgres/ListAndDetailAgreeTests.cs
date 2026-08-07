using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Application.Validators;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Escalations;
using Callu.Shared.Models.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>A row on a list screen has to say the same thing the detail screen says about it.</summary>
// List reads are written separately from detail reads and keep ending up with fewer includes, and
// the shortfall never surfaces as an error: a name renders blank, a count renders zero, a row is
// hidden by a filter that matched on the missing value. The operator sees a screen that looks fine.
[Collection(PostgresCollection.Name)]
public class ListAndDetailAgreeTests(PostgresFixture pg)
{
    // ---- services ----------------------------------------------------------

    [PostgresFact]
    public async Task EveryServiceListReadAgreesWithTheDetailRead()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (serviceId, teamId) = await SeedServiceAsync(cs);

        await using var db = PostgresFixture.Context(cs);
        var service = ServiceManagement(db, serviceId);

        var detail = (await service.GetByIdAsync(serviceId)).Value!;
        var all = (await service.GetAllAsync()).Value!.Items.Single(s => s.Id == serviceId);
        var byTeam = (await service.GetByTeamAsync(teamId)).Value!.Single(s => s.Id == serviceId);
        var byStatus = (await service.GetByStatusAsync(ServiceStatus.Operational)).Value!.Single(s => s.Id == serviceId);

        foreach (var listed in new[] { all, byTeam, byStatus })
            AssertAgrees(detail, listed);
    }

    private static void AssertAgrees(ServiceDto detail, ServiceListDto listed)
    {
        Assert.Equal(detail.Name, listed.Name);
        Assert.Equal(detail.Description, listed.Description);
        Assert.Equal(detail.Type, listed.Type.ToString());
        Assert.Equal(detail.Status, listed.Status.ToString());
        Assert.Equal(detail.TeamName, listed.TeamName);
        Assert.Equal(detail.Uptime, listed.Uptime);
        Assert.Equal(detail.IncidentCount, listed.IncidentCount);
    }

    // ---- escalation policies -----------------------------------------------

    /// <summary>The two reads return the same shape, so nothing about them may differ.</summary>
    [PostgresFact]
    public async Task EveryEscalationPolicyListReadAgreesWithTheDetailRead()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var (policyId, scheduleId, teamId) = await SeedPolicyAsync(cs);

        await using (var seed = PostgresFixture.Context(cs))
        {
            seed.Add(new EscalationStep
            {
                EscalationPolicyId = policyId,
                Level = 1,
                Title = "Primary on-call",
                DelayMinutes = 0,
                ScheduleId = scheduleId,
            });
            seed.Add(new EscalationStep
            {
                EscalationPolicyId = policyId,
                Level = 2,
                Title = "The whole team",
                DelayMinutes = 15,
                TeamId = teamId,
                NotifyAllTeamMembers = true,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = PostgresFixture.Context(cs);
        var service = Escalations(db);

        var detail = await service.GetEscalationPolicyByIdAsync(policyId);
        var listed = (await service.GetEscalationPoliciesAsync()).Single(p => p.Id == policyId);
        var byTeam = (await service.GetEscalationPoliciesByTeamAsync(teamId)).Single(p => p.Id == policyId);

        Assert.NotNull(detail);
        AssertAgrees(detail!, listed);
        AssertAgrees(detail!, byTeam);
    }

    private static void AssertAgrees(EscalationDto detail, EscalationDto listed)
    {
        Assert.Equal(detail.Name, listed.Name);
        Assert.Equal(detail.Description, listed.Description);
        Assert.Equal(detail.TeamId, listed.TeamId);
        Assert.Equal(detail.TeamName, listed.TeamName);
        Assert.Equal(detail.IsActive, listed.IsActive);
        Assert.Equal(detail.ExhaustionBehavior, listed.ExhaustionBehavior);
        Assert.Equal(detail.MaxRepeatCycles, listed.MaxRepeatCycles);
        Assert.Equal(detail.MaxRepeatDurationMinutes, listed.MaxRepeatDurationMinutes);
        Assert.Equal(detail.StepCount, listed.StepCount);

        var detailSteps = detail.Steps.ToArray();
        var listedSteps = listed.Steps.ToArray();
        Assert.Equal(detailSteps.Length, listedSteps.Length);

        for (var i = 0; i < detailSteps.Length; i++)
        {
            Assert.Equal(detailSteps[i].Id, listedSteps[i].Id);
            Assert.Equal(detailSteps[i].Level, listedSteps[i].Level);
            Assert.Equal(detailSteps[i].Title, listedSteps[i].Title);
            Assert.Equal(detailSteps[i].DelayMinutes, listedSteps[i].DelayMinutes);
            Assert.Equal(detailSteps[i].ScheduleId, listedSteps[i].ScheduleId);
            Assert.Equal(detailSteps[i].ScheduleName, listedSteps[i].ScheduleName);
            Assert.Equal(detailSteps[i].TeamId, listedSteps[i].TeamId);
            Assert.Equal(detailSteps[i].TeamName, listedSteps[i].TeamName);
            Assert.Equal(detailSteps[i].NotifyAllTeamMembers, listedSteps[i].NotifyAllTeamMembers);
            Assert.Equal(detailSteps[i].NotifyBothOnCall, listedSteps[i].NotifyBothOnCall);
            Assert.Equal(detailSteps[i].NotifyUserIds, listedSteps[i].NotifyUserIds);
            Assert.Equal(detailSteps[i].NotifyUserNames, listedSteps[i].NotifyUserNames);
        }
    }

    // ---- harness -----------------------------------------------------------

    // Real repositories against the real database; only the collaborators that reach outside it are
    // substituted, and the uptime one returns a value so an unpopulated field cannot pass as a match.
    private static IServiceManagementService ServiceManagement(ApplicationDbContext db, Guid serviceId)
    {
        var uptime = Substitute.For<IUptimeCalculator>();
        uptime.ComputeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ServiceUptimeResult>>(
                _ => [new ServiceUptimeResult(serviceId, "Checkout API", 2, 12.5, 99.42)]);

        return new ServiceManagementService(
            new ServiceRepository(db, NullLogger<ServiceRepository>.Instance),
            Substitute.For<IServiceDependencyRepository>(),
            new TransactionManager(db, NullLogger<TransactionManager>.Instance),
            Substitute.For<IAuditLogService>(),
            Substitute.For<IServiceStatusCascadeEngine>(),
            Substitute.For<IStatusPageComponentService>(),
            uptime,
            Substitute.For<IIncidentService>(),
            NullLogger<ServiceManagementService>.Instance);
    }

    private static IEscalationService Escalations(ApplicationDbContext db)
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

    private static async Task<(Guid ServiceId, Guid TeamId)> SeedServiceAsync(string connectionString)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var team = new Team { Id = Guid.NewGuid(), Name = $"Payments {Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
        var service = new Service
        {
            Id = Guid.NewGuid(),
            Name = "Checkout API",
            Description = "Takes the money",
            Type = ServiceType.Api,
            Status = ServiceStatus.Operational,
            TeamId = team.Id,
            CreatedAt = DateTime.UtcNow,
        };

        db.Teams.Add(team);
        db.Services.Add(service);
        db.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(),
            Title = "Checkout is down",
            Severity = IncidentSeverity.High,
            Status = IncidentStatus.Open,
            ServiceId = service.Id,
            TeamId = team.Id,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return (service.Id, team.Id);
    }

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
            CreatedAt = DateTime.UtcNow,
        };
        var policy = new EscalationPolicy
        {
            Id = Guid.NewGuid(),
            Name = $"Payments paging {Guid.NewGuid():N}",
            Description = "Who gets woken up",
            TeamId = team.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        db.Teams.Add(team);
        db.Schedules.Add(schedule);
        db.EscalationPolicies.Add(policy);
        await db.SaveChangesAsync();

        return (policy.Id, schedule.Id, team.Id);
    }
}
