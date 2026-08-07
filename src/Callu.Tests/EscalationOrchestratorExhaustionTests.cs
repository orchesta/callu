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

/// <summary>Covers when the last step's call decides whether escalation is exhausted or still ringing.</summary>
public class EscalationOrchestratorExhaustionTests
{
    private const string ExhaustedTimelineTitle = "Escalation exhausted";
    private const AuditAction ExhaustedAuditAction = AuditAction.EscalationExhausted;

    private sealed class Harness
    {
        public ApplicationDbContext Ctx { get; }
        public EscalationOrchestrator Sut { get; }
        public IAuditLogService Audit { get; }

        public Harness()
        {
            Ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"exhaust-{Guid.NewGuid():N}").Options);
            Audit = Substitute.For<IAuditLogService>();
            var dispatcher = Substitute.For<INotificationDispatcher>();
            dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
                .Returns(new NotificationDispatchResult(1, 0));

            Sut = new EscalationOrchestrator(
                new IncidentRepository(Ctx, NullLogger<IncidentRepository>.Instance),
                new EscalationPolicyRepository(Ctx, NullLogger<EscalationPolicyRepository>.Instance),
                new IncidentTimelineEventRepository(Ctx, NullLogger<IncidentTimelineEventRepository>.Instance),
                new CallLogRepository(Ctx, NullLogger<CallLogRepository>.Instance),
                Audit,
                new SavingTransactionManager(Ctx),
                dispatcher,
                new CalluMetrics(new FakeMeterFactory()),
                NullLogger<EscalationOrchestrator>.Instance);
        }

        public Task<bool> HasExhaustedTimelineAsync(Guid incidentId) =>
            Ctx.Set<IncidentTimelineEvent>().AnyAsync(e => e.IncidentId == incidentId && e.Title == ExhaustedTimelineTitle);

        public bool HasExhaustedAudit(Guid incidentId) =>
            Audit.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IAuditLogService.LogAsync))
                .Select(c => c.GetArguments())
                .Any(a => (AuditAction?)a[1] == ExhaustedAuditAction && (string?)a[3] == incidentId.ToString());

        public Task<Incident> ReloadAsync(Guid id) =>
            Ctx.Incidents.AsNoTracking().FirstAsync(i => i.Id == id);
    }

    /// <summary>An incident whose only step has already been paged, so the next tick lands on exhaustion.</summary>
    private static async Task<(Harness Harness, Incident Incident)> SeedOnLastStepAsync(CallStatus callStatus, TimeSpan callAge)
    {
        var h = new Harness();
        var step = new EscalationStep
        {
            Id = Guid.NewGuid(),
            Level = 1,
            DelayMinutes = 0,
            TargetedUsers = new List<EscalationStepUser> { new() { UserId = "u1" } }
        };
        var policy = new EscalationPolicy { Id = Guid.NewGuid(), Name = "policy", IsActive = true, Steps = [step] };

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "DB unreachable",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            CurrentEscalationStepId = step.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-30),
            LastEscalationStepAt = DateTime.UtcNow.AddMinutes(-30)
        };

        h.Ctx.Add(policy);
        h.Ctx.Add(incident);
        h.Ctx.Add(new CallLog
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            PhoneNumber = "+900000000000",
            Status = callStatus,
            InitiatedAt = DateTime.UtcNow - callAge
        });
        await h.Ctx.SaveChangesAsync();

        return (h, incident);
    }

    [Fact]
    public async Task RingingCall_OnTheLastStep_DoesNotRecordExhaustion()
    {
        var (h, incident) = await SeedOnLastStepAsync(CallStatus.Initiated, TimeSpan.FromSeconds(10));

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.False(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.False(h.HasExhaustedAudit(incident.Id));
        // The run must stay open, otherwise nothing re-evaluates it once the call settles.
        Assert.True((await h.ReloadAsync(incident.Id)).IsEscalationActive);
    }

    [Fact]
    public async Task ConnectedCall_OnTheLastStep_DoesNotRecordExhaustion()
    {
        var (h, incident) = await SeedOnLastStepAsync(CallStatus.Connected, TimeSpan.FromSeconds(20));

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.False(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.False(h.HasExhaustedAudit(incident.Id));
    }

    [Fact]
    public async Task DeferredRun_RecordsNothing_WhenTheResponderAcknowledges()
    {
        var (h, incident) = await SeedOnLastStepAsync(CallStatus.Initiated, TimeSpan.FromSeconds(10));

        await h.Sut.ProcessPendingEscalationsAsync();

        var call = await h.Ctx.Set<CallLog>().FirstAsync(c => c.IncidentId == incident.Id);
        call.Status = CallStatus.Acknowledged;
        var tracked = await h.Ctx.Incidents.FirstAsync(i => i.Id == incident.Id);
        tracked.Status = IncidentStatus.Acknowledged;
        await h.Ctx.SaveChangesAsync();

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.False(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.False(h.HasExhaustedAudit(incident.Id));
    }

    [Fact]
    public async Task UnansweredCall_OnTheLastStep_RecordsExhaustion()
    {
        var (h, incident) = await SeedOnLastStepAsync(CallStatus.NoAnswer, TimeSpan.FromMinutes(1));

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.True(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.True(h.HasExhaustedAudit(incident.Id));
        Assert.False((await h.ReloadAsync(incident.Id)).IsEscalationActive);
    }

    /// <summary>A call stuck on Initiated must not postpone the record forever — no callback is ever coming.</summary>
    [Fact]
    public async Task StaleRingingCall_RecordsExhaustion()
    {
        var (h, incident) = await SeedOnLastStepAsync(CallStatus.Initiated, TimeSpan.FromHours(2));

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.True(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.True(h.HasExhaustedAudit(incident.Id));
        Assert.False((await h.ReloadAsync(incident.Id)).IsEscalationActive);
    }

    /// <summary>No call at all is the pre-existing behaviour and stays unchanged.</summary>
    [Fact]
    public async Task NoCallLogged_RecordsExhaustion()
    {
        var (h, incident) = await SeedOnLastStepAsync(CallStatus.NoAnswer, TimeSpan.FromMinutes(1));
        h.Ctx.RemoveRange(await h.Ctx.Set<CallLog>().ToListAsync());
        await h.Ctx.SaveChangesAsync();

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.True(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.True(h.HasExhaustedAudit(incident.Id));
    }

    /// <summary>A call for a different incident is not this incident's page.</summary>
    [Fact]
    public async Task RingingCall_ForAnotherIncident_DoesNotDeferThisOne()
    {
        var (h, incident) = await SeedOnLastStepAsync(CallStatus.NoAnswer, TimeSpan.FromMinutes(1));
        h.Ctx.Add(new CallLog
        {
            Id = Guid.NewGuid(),
            IncidentId = Guid.NewGuid(),
            PhoneNumber = "+900000000001",
            Status = CallStatus.Initiated,
            InitiatedAt = DateTime.UtcNow
        });
        await h.Ctx.SaveChangesAsync();

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.True(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.True(h.HasExhaustedAudit(incident.Id));
    }

    private static async Task<(Harness Harness, Incident Incident, EscalationPolicy Policy)> SeedRepeatOnLastStepAsync(
        int maxCycles,
        int maxDurationMinutes,
        DateTime? startedAt = null)
    {
        var h = new Harness();
        var step = new EscalationStep
        {
            Id = Guid.NewGuid(),
            Level = 1,
            DelayMinutes = 0,
            TargetedUsers = new List<EscalationStepUser> { new() { UserId = "u1" } }
        };
        var policy = new EscalationPolicy
        {
            Id = Guid.NewGuid(),
            Name = "repeat-policy",
            IsActive = true,
            ExhaustionBehavior = EscalationExhaustionBehavior.Repeat,
            MaxRepeatCycles = maxCycles,
            MaxRepeatDurationMinutes = maxDurationMinutes,
            Steps = [step]
        };

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "DB unreachable",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            CurrentEscalationStepId = step.Id,
            EscalationStartedAt = startedAt ?? DateTime.UtcNow.AddMinutes(-30),
            LastEscalationStepAt = DateTime.UtcNow.AddMinutes(-30),
            EscalationCyclesCompleted = 0
        };

        h.Ctx.Add(policy);
        h.Ctx.Add(incident);
        h.Ctx.Add(new CallLog
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            PhoneNumber = "+900000000000",
            Status = CallStatus.NoAnswer,
            InitiatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await h.Ctx.SaveChangesAsync();

        return (h, incident, policy);
    }

    [Fact]
    public async Task RepeatPolicy_OnLastStep_RestartsInsteadOfExhausting()
    {
        var (h, incident, _) = await SeedRepeatOnLastStepAsync(maxCycles: 3, maxDurationMinutes: 240);

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.False(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.False(h.HasExhaustedAudit(incident.Id));
        var reloaded = await h.ReloadAsync(incident.Id);
        Assert.True(reloaded.IsEscalationActive);
        Assert.Null(reloaded.CurrentEscalationStepId);
        Assert.Equal(1, reloaded.EscalationCyclesCompleted);
        Assert.True(await h.Ctx.Set<IncidentTimelineEvent>()
            .AnyAsync(e => e.IncidentId == incident.Id && e.Title == "Escalation repeating"));
    }

    [Fact]
    public async Task RepeatPolicy_AtMaxCycles_ExhaustsWithoutAutoAck()
    {
        var (h, incident, _) = await SeedRepeatOnLastStepAsync(maxCycles: 1, maxDurationMinutes: 240);

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.True(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.True(h.HasExhaustedAudit(incident.Id));
        var reloaded = await h.ReloadAsync(incident.Id);
        Assert.False(reloaded.IsEscalationActive);
        Assert.Equal(IncidentStatus.Open, reloaded.Status);
        Assert.Null(reloaded.AcknowledgedAt);
    }

    [Fact]
    public async Task RepeatPolicy_PastMaxDuration_Exhausts()
    {
        var (h, incident, _) = await SeedRepeatOnLastStepAsync(
            maxCycles: 10,
            maxDurationMinutes: 60,
            startedAt: DateTime.UtcNow.AddHours(-2));

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.True(await h.HasExhaustedTimelineAsync(incident.Id));
        Assert.True(h.HasExhaustedAudit(incident.Id));
        Assert.False((await h.ReloadAsync(incident.Id)).IsEscalationActive);
    }
}
