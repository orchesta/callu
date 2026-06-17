using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// Integration-style coverage of the escalation advance state machine over the EF in-memory
/// provider with real repositories — the part not reachable through the unit-level
/// ResolveTarget/DispatchTarget tests. Notifications are mocked; everything else is real.
/// </summary>
public class EscalationOrchestratorAdvanceTests
{
    private sealed class Harness
    {
        public ApplicationDbContext Ctx { get; }
        public EscalationOrchestrator Sut { get; }
        public INotificationDispatcher Dispatcher { get; }

        public Harness()
        {
            Ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"esc-{Guid.NewGuid():N}").Options);
            Dispatcher = Substitute.For<INotificationDispatcher>();
            Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
                .Returns(1);
            Sut = new EscalationOrchestrator(
                new IncidentRepository(Ctx, NullLogger<IncidentRepository>.Instance),
                new IncidentTimelineEventRepository(Ctx, NullLogger<IncidentTimelineEventRepository>.Instance),
                Substitute.For<IAuditLogService>(),
                new SavingTransactionManager(Ctx),
                Dispatcher,
                new CalluMetrics(new FakeMeterFactory()),
                NullLogger<EscalationOrchestrator>.Instance);
        }

        public async Task SeedAsync(Incident incident, EscalationPolicy? policy)
        {
            if (policy is not null) Ctx.Add(policy);
            Ctx.Add(incident);
            await Ctx.SaveChangesAsync();
        }

        public Task<Incident> ReloadAsync(Guid id) =>
            Ctx.Incidents.AsNoTracking().FirstAsync(i => i.Id == id);

        public Task<bool> HasTimelineAsync(Guid incidentId, string titleFragment) =>
            Ctx.Set<IncidentTimelineEvent>().AnyAsync(e => e.IncidentId == incidentId && e.Title.Contains(titleFragment));
    }

    private static EscalationStep Step(int level, int delayMinutes, string userId) => new()
    {
        Id = Guid.NewGuid(),
        Level = level,
        DelayMinutes = delayMinutes,
        TargetedUsers = new List<EscalationStepUser> { new() { UserId = userId } }
    };

    private static EscalationPolicy Policy(params EscalationStep[] steps) => new()
    {
        Id = Guid.NewGuid(),
        Name = "policy",
        IsActive = true,
        Steps = steps.ToList()
    };

    [Fact]
    public async Task FirstStep_TriggersWhenDelayElapsed_AdvancesPointerAndDispatches()
    {
        var h = new Harness();
        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "DB unreachable",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, updated.CurrentEscalationStepId);
        Assert.NotNull(updated.LastEscalationStepAt);
        Assert.True(updated.IsEscalationActive);
        Assert.True(await h.HasTimelineAsync(incident.Id, "step 1 triggered"));
        await h.Dispatcher.Received(1).NotifyUsersAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.Contains("u1")),
            Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FirstStep_DoesNotTrigger_BeforeDelayElapses()
    {
        var h = new Harness();
        var policy = Policy(Step(1, 60, "u1"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Null(updated.CurrentEscalationStepId);
        await h.Dispatcher.DidNotReceive().NotifyUsersAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LastStepReached_ExhaustsAndDeactivates()
    {
        var h = new Harness();
        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            CurrentEscalationStepId = step1.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-30),
            LastEscalationStepAt = DateTime.UtcNow.AddMinutes(-10)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.False(updated.IsEscalationActive);
        Assert.True(await h.HasTimelineAsync(incident.Id, "exhausted"));
    }

    [Fact]
    public async Task AdvanceEscalation_BackdatesLastStep_ForActiveIncident()
    {
        var h = new Harness();
        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            CurrentEscalationStepId = step1.Id,
            LastEscalationStepAt = DateTime.UtcNow
        };
        await h.SeedAsync(incident, policy);

        var result = await h.Sut.AdvanceEscalationAsync(incident.Id);

        Assert.True(result);
        var updated = await h.ReloadAsync(incident.Id);
        Assert.True(updated.LastEscalationStepAt < DateTime.UtcNow.AddHours(-1));
    }

    [Fact]
    public async Task AdvanceEscalation_ReturnsFalse_WhenEscalationNotActive()
    {
        var h = new Harness();
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = false
        };
        await h.SeedAsync(incident, policy: null);

        Assert.False(await h.Sut.AdvanceEscalationAsync(incident.Id));
    }

    [Fact]
    public async Task TriggerEscalation_AlreadyActiveSamePolicy_DoesNotResetStep()
    {
        var h = new Harness();
        var policyId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policyId,
            CurrentEscalationStepId = stepId
        };
        await h.SeedAsync(incident, policy: null);

        await h.Sut.TriggerEscalationAsync(incident.Id, policyId);

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Equal(stepId, updated.CurrentEscalationStepId);
    }

    [Fact]
    public async Task TriggerEscalation_OnAcknowledgedIncident_IsIgnored()
    {
        var h = new Harness();
        var policyId = Guid.NewGuid();
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Acknowledged,
            IsEscalationActive = false,
            EscalationPolicyId = policyId
        };
        await h.SeedAsync(incident, policy: null);

        await h.Sut.TriggerEscalationAsync(incident.Id, policyId);

        var updated = await h.ReloadAsync(incident.Id);
        Assert.False(updated.IsEscalationActive);
    }
}
