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

/// <summary>Integration-style coverage of the escalation advance state machine over real repositories.</summary>
public class EscalationOrchestratorAdvanceTests
{
    private sealed class Harness
    {
        public ApplicationDbContext Ctx { get; }
        public EscalationOrchestrator Sut { get; }
        public INotificationDispatcher Dispatcher { get; }
        public IAuditLogService Audit { get; }

        public Harness()
        {
            Ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"esc-{Guid.NewGuid():N}").Options);
            Dispatcher = Substitute.For<INotificationDispatcher>();
            Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
                .Returns(new NotificationDispatchResult(1, 0));
            Audit = Substitute.For<IAuditLogService>();
            Sut = new EscalationOrchestrator(
                new IncidentRepository(Ctx, NullLogger<IncidentRepository>.Instance),
                new EscalationPolicyRepository(Ctx, NullLogger<EscalationPolicyRepository>.Instance),
                new IncidentTimelineEventRepository(Ctx, NullLogger<IncidentTimelineEventRepository>.Instance),
                new CallLogRepository(Ctx, NullLogger<CallLogRepository>.Instance),
                Audit,
                new SavingTransactionManager(Ctx),
                Dispatcher,
                new CalluMetrics(new FakeMeterFactory()),
                NullLogger<EscalationOrchestrator>.Instance);
        }

        /// <summary>The audit trail is a separate surface from the timeline, and a lost page has to be on both.</summary>
        public async Task<string> AuditDetailAsync(Guid incidentId, AuditAction action)
        {
            var calls = Audit.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IAuditLogService.LogAsync))
                .Select(c => c.GetArguments())
                .Where(a => (AuditAction?)a[1] == action && (string?)a[3] == incidentId.ToString())
                .ToList();

            Assert.True(calls.Count == 1,
                $"expected exactly one '{action}' audit entry for the incident, found {calls.Count}");

            return await Task.FromResult((string?)calls[0][5] ?? string.Empty);
        }

        public bool HasAudit(Guid incidentId, AuditAction action) =>
            Audit.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IAuditLogService.LogAsync))
                .Select(c => c.GetArguments())
                .Any(a => (AuditAction?)a[1] == action && (string?)a[3] == incidentId.ToString());

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

        public async Task<string> TimelineDescriptionAsync(Guid incidentId, string titleFragment) =>
            (await Ctx.Set<IncidentTimelineEvent>()
                .FirstAsync(e => e.IncidentId == incidentId && e.Title.Contains(titleFragment)))
            .Description ?? string.Empty;
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

    [Fact]
    public async Task FirstStep_DispatchesAndAdvancesPointer_EvenWhenNobodyReached()
    {
        var h = new Harness();
        h.Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(NotificationDispatchResult.Nobody);
        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, updated.CurrentEscalationStepId);
        Assert.True(updated.IsEscalationActive);
        Assert.True(await h.HasTimelineAsync(incident.Id, "nobody paged"));
        await h.Dispatcher.Received(1).NotifyUsersAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A failed dispatch is not a step that ran, so the pointer stays where it was.</summary>
    [Fact]
    public async Task DispatchFailed_LeavesTheStepPointerAlone_SoTheNextTickPagesTheSameStepAgain()
    {
        var h = new Harness();
        h.Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationDispatchResult(0, 1));

        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var afterFailure = await h.ReloadAsync(incident.Id);
        Assert.Null(afterFailure.CurrentEscalationStepId);
        Assert.Null(afterFailure.LastEscalationStepAt);
        Assert.True(afterFailure.IsEscalationActive);
        Assert.False(await h.HasTimelineAsync(incident.Id, "nobody paged"));
        Assert.False(await h.HasTimelineAsync(incident.Id, "step 1 triggered"));

        // The first failure is stamped on the incident — that stamp, not the step's due time, is
        // what the retry window is measured from.
        Assert.NotNull(afterFailure.DispatchFailingSince);
        Assert.True(afterFailure.DispatchFailingSince > DateTime.UtcNow.AddMinutes(-1));
        Assert.True(await h.HasTimelineAsync(incident.Id, "could not be queued (retrying)"));

        // The notification store recovers; the same step pages for real on the next tick.
        h.Ctx.ChangeTracker.Clear();
        h.Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationDispatchResult(1, 0));

        await h.Sut.ProcessPendingEscalationsAsync();

        var afterRecovery = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, afterRecovery.CurrentEscalationStepId);
        Assert.True(await h.HasTimelineAsync(incident.Id, "step 1 triggered"));
        Assert.Null(afterRecovery.DispatchFailingSince);
        await h.Dispatcher.Received(2).NotifyUsersAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.Contains("u1")),
            Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Past the retry window the step is given up on, so the later steps stay reachable.</summary>
    [Fact]
    public async Task DispatchFailed_PastTheRetryWindow_GivesUpOnTheStepAndKeepsTheLaterStepsAlive()
    {
        var h = new Harness();
        h.Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationDispatchResult(0, 1));

        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddHours(-1),
            // Step 1 has been *failing* — not merely due — for 20 minutes.
            DispatchFailingSince = DateTime.UtcNow.AddMinutes(-20)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, updated.CurrentEscalationStepId);
        Assert.True(updated.IsEscalationActive);
        Assert.True(await h.HasTimelineAsync(incident.Id, "could not be queued (gave up)"));
        Assert.False(await h.HasTimelineAsync(incident.Id, "nobody paged"));

        // The window is cleared with the step: step 2 gets its own 15 minutes to fail in.
        Assert.Null(updated.DispatchFailingSince);

        // Not backdated: the step's page never went out, so there is no ack window to cut short.
        Assert.True(updated.LastEscalationStepAt > DateTime.UtcNow.AddMinutes(-1));

        // Giving up on a step is a thing the post-mortem must be able to find. Silently moving on is
        // how "why did nobody get called?" becomes unanswerable a week later.
        var audited = await h.AuditDetailAsync(incident.Id, AuditAction.EscalationDispatchFailed);
        Assert.Contains("Step 1", audited);
        Assert.False(h.HasAudit(incident.Id, AuditAction.EscalationDispatchPartiallyFailed));
    }

    /// <summary>A manually escalated step whose dispatch fails is retried, because the window runs from the first failure.</summary>
    [Fact]
    public async Task ManuallyEscalatedStep_WhoseDispatchFails_IsRetried_NotBurnedOnItsFirstAttempt()
    {
        var h = new Harness();
        var step1 = Step(1, 0, "u1");
        var step2 = Step(2, 30, "u2");
        var policy = Policy(step1, step2);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            CurrentEscalationStepId = step1.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-5),
            LastEscalationStepAt = DateTime.UtcNow.AddMinutes(-5)
        };
        await h.SeedAsync(incident, policy);

        // The operator presses Escalate: step 2 is now due, a day "overdue" by the old measure.
        Assert.True(await h.Sut.AdvanceEscalationAsync(incident.Id));
        h.Ctx.ChangeTracker.Clear();

        h.Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationDispatchResult(0, 1));

        await h.Sut.ProcessPendingEscalationsAsync();

        var afterFailure = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, afterFailure.CurrentEscalationStepId);
        Assert.False(await h.HasTimelineAsync(incident.Id, "gave up"));
        Assert.NotNull(afterFailure.DispatchFailingSince);

        // The store recovers on the next tick and step 2 actually pages someone.
        h.Ctx.ChangeTracker.Clear();
        h.Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationDispatchResult(1, 0));

        await h.Sut.ProcessPendingEscalationsAsync();

        var afterRecovery = await h.ReloadAsync(incident.Id);
        Assert.Equal(step2.Id, afterRecovery.CurrentEscalationStepId);
        Assert.Null(afterRecovery.DispatchFailingSince);
        await h.Dispatcher.Received(2).NotifyUsersAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.Contains("u2")),
            Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A partial dispatch failure advances the step and is recorded on both surfaces rather than swallowed.</summary>
    [Fact]
    public async Task PartialDispatchFailure_AdvancesTheStep_ButIsRecordedRatherThanSwallowed()
    {
        var h = new Harness();
        h.Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationDispatchResult(1, 0,
                [new DispatchChannelFailure("u1", NotificationType.VoiceCall)]));

        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, updated.CurrentEscalationStepId);
        Assert.True(updated.IsEscalationActive);
        Assert.True(await h.HasTimelineAsync(incident.Id, "step 1 triggered"));

        Assert.True(await h.HasTimelineAsync(incident.Id, "some pages could not be queued"));
        Assert.False(await h.HasTimelineAsync(incident.Id, "nobody paged"));
        Assert.False(await h.HasTimelineAsync(incident.Id, "gave up"));

        var lost = await h.TimelineDescriptionAsync(incident.Id, "some pages could not be queued");
        Assert.Contains("VoiceCall", lost);

        // ...and the post-mortem can see it too, with the channel named: "the step went out" and
        // "u1's phone rang" are different claims, and only the first one is true here.
        var audited = await h.AuditDetailAsync(incident.Id, AuditAction.EscalationDispatchPartiallyFailed);
        Assert.Contains("VoiceCall", audited);
        Assert.Contains("reached 1", audited);
        Assert.Contains("lost 1", audited);

        // A partial loss is not a total one — claiming the step paged nobody would be its own lie.
        Assert.False(h.HasAudit(incident.Id, AuditAction.EscalationDispatchFailed));
    }

    [Fact]
    public async Task StepAcknowledgedDuringDispatch_DoesNotAdvancePointer()
    {
        var h = new Harness();
        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        h.Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var inFlight = h.Ctx.Incidents.First(i => i.Id == incident.Id);
                inFlight.Status = IncidentStatus.Acknowledged;
                inFlight.IsEscalationActive = false;
                h.Ctx.SaveChanges();
                return new NotificationDispatchResult(1, 0);
            });

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Null(updated.CurrentEscalationStepId);
        Assert.False(updated.IsEscalationActive);
    }

    // ---- targets unpageable, at the ORCHESTRATOR ---------------------------
    //
    // The distinction: nobody is on call (fix the rota) versus somebody is and nothing can wake them
    // (fix their profile). Same silence, opposite ends of the system.

    /// <summary>A step aimed at a schedule — the shape that backdates, and the shape that used to lie.</summary>
    private static EscalationStep ScheduleStep(int level, int delayMinutes, Guid scheduleId) => new()
    {
        Id = Guid.NewGuid(),
        Level = level,
        DelayMinutes = delayMinutes,
        ScheduleId = scheduleId
    };

    /// <summary>
    /// A dispatch that reached nobody because the on-call responder has no channel that can page them:
    /// nothing queued, nothing failed, one unpageable target.
    /// </summary>
    private static NotificationDispatchResult Unpageable(string userId = "u1", string reason = "no phone number on file") =>
        new(0, 0, Unpageable: 1, UnpageableTargets: [new DispatchUnpageableTarget(userId, reason)]);

    /// <summary>An unpageable target is recorded as a profile fault, naming the responder and the missing thing.</summary>
    [Fact]
    public async Task TargetsUnpageable_IsRecordedAsAProfileFault_NotAsAnEmptyRota()
    {
        var h = new Harness();
        var scheduleId = Guid.NewGuid();
        h.Dispatcher.NotifyOnCallAsync(scheduleId, Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(Unpageable("carol", "no phone number on file"));

        var step1 = ScheduleStep(1, 0, scheduleId);
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "Checkout failing",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        // The honest record, with the fault named on it.
        Assert.True(await h.HasTimelineAsync(incident.Id, "nobody could be paged (the responder has no channel that can page them)"));
        var told = await h.TimelineDescriptionAsync(incident.Id, "nobody could be paged (the responder has no channel");
        Assert.Contains("carol", told);
        Assert.Contains("no phone number on file", told);
        Assert.Contains("NOT an empty on-call rota", told);

        // ...and NOT the empty-rota one. This is the assertion the whole file exists for.
        Assert.False(await h.HasTimelineAsync(incident.Id, "nobody paged"));

        // Both surfaces: the timeline the operator watches, the audit log the post-mortem reads.
        var audited = await h.AuditDetailAsync(incident.Id, AuditAction.EscalationTargetsUnpageable);
        Assert.Contains("carol", audited);
        Assert.False(h.HasAudit(incident.Id, AuditAction.EscalationNobodyReached));

        // Nor is it a store error — nothing here is worth re-running.
        Assert.False(h.HasAudit(incident.Id, AuditAction.EscalationDispatchFailed));
        Assert.False(await h.HasTimelineAsync(incident.Id, "could not be queued"));
    }

    /// <summary>A step nothing could page advances and backdates, so it cannot wedge the rest of the ladder.</summary>
    [Fact]
    public async Task AFullyUnpageableScheduleStep_AdvancesAndBackdates_SoTheNextStepIsNotMadeToWaitOnADeadPage()
    {
        var h = new Harness();
        var scheduleId = Guid.NewGuid();
        h.Dispatcher.NotifyOnCallAsync(scheduleId, Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(Unpageable());

        var step1 = ScheduleStep(1, 0, scheduleId);
        var step2 = Step(2, 0, "u2");
        var policy = Policy(step1, step2);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "Checkout failing",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var afterStep1 = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, afterStep1.CurrentEscalationStepId);
        Assert.True(afterStep1.IsEscalationActive, "an unpageable responder must not switch escalation off");

        // Backdated: there is nothing to wait FOR, so step 1's delay is not served out.
        Assert.NotNull(afterStep1.LastEscalationStepAt);
        Assert.True(afterStep1.LastEscalationStepAt < DateTime.UtcNow.AddMinutes(-2),
            "the step clock must be backdated — no page is in flight, so nothing can arrive to justify waiting");

        // ...and the proof that it means something: the very next tick pages step 2, ~10 seconds after
        // step 1 rather than the 2-minute floor away. Without the backdate, one profile gap costs the
        // dead step's entire wait before anybody who CAN be paged is tried.
        h.Ctx.ChangeTracker.Clear();
        await h.Sut.ProcessPendingEscalationsAsync();

        var afterStep2 = await h.ReloadAsync(incident.Id);
        Assert.Equal(step2.Id, afterStep2.CurrentEscalationStepId);
        await h.Dispatcher.Received(1).NotifyUsersAsync(
            Arg.Is<IEnumerable<string>>(ids => ids.Contains("u2")),
            Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An unpageable Users step advances without the backdate, so the configured gap is served out.</summary>
    [Fact]
    public async Task AFullyUnpageableUsersStep_Advances_ButDoesNotBackdateTheClock()
    {
        var h = new Harness();
        h.Dispatcher.NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(Unpageable());

        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, updated.CurrentEscalationStepId);
        Assert.True(updated.IsEscalationActive);

        Assert.NotNull(updated.LastEscalationStepAt);
        Assert.True(updated.LastEscalationStepAt > DateTime.UtcNow.AddMinutes(-1),
            "a Users step is not short-circuited to the next tick — step 2's delay is served out as configured");

        Assert.True(await h.HasTimelineAsync(incident.Id, "nobody could be paged (the responder has no channel that can page them)"));
        Assert.False(await h.HasTimelineAsync(incident.Id, "nobody paged"));
    }

    /// <summary>A genuinely empty rota still reports nobody paged and claims no unpageable target.</summary>
    [Fact]
    public async Task AGenuinelyEmptyRota_StillSaysNobodyPaged_AndClaimsNoUnpageableTarget()
    {
        var h = new Harness();
        var scheduleId = Guid.NewGuid();
        h.Dispatcher.NotifyOnCallAsync(scheduleId, Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(NotificationDispatchResult.Nobody);

        var step1 = ScheduleStep(1, 0, scheduleId);
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        Assert.True(await h.HasTimelineAsync(incident.Id, "nobody paged"));
        Assert.False(await h.HasTimelineAsync(incident.Id, "nobody could be paged"));
        Assert.True(h.HasAudit(incident.Id, AuditAction.EscalationNobodyReached));
        Assert.False(h.HasAudit(incident.Id, AuditAction.EscalationTargetsUnpageable));
    }

    /// <summary>One unpageable responder among reached ones advances normally and claims no total silence.</summary>
    [Fact]
    public async Task OneUnpageableResponderAmongReachedOnes_AdvancesNormally_AndClaimsNoTotalSilence()
    {
        var h = new Harness();
        var scheduleId = Guid.NewGuid();
        h.Dispatcher.NotifyOnCallAsync(scheduleId, Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationDispatchResult(1, 0, Unpageable: 1,
                UnpageableTargets: [new DispatchUnpageableTarget("secondary", "push only")]));

        var step1 = ScheduleStep(1, 0, scheduleId);
        var policy = Policy(step1, Step(2, 5, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "t",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, updated.CurrentEscalationStepId);
        Assert.True(await h.HasTimelineAsync(incident.Id, "step 1 triggered"));

        // A page IS in flight — the clock must run normally so the responder gets their ack window.
        Assert.NotNull(updated.LastEscalationStepAt);
        Assert.True(updated.LastEscalationStepAt > DateTime.UtcNow.AddMinutes(-1),
            "somebody was paged: backdating here would fire step 2 ten seconds later, on top of a live page");

        Assert.False(await h.HasTimelineAsync(incident.Id, "nobody could be paged"));
        Assert.False(await h.HasTimelineAsync(incident.Id, "nobody paged"));
    }

    private static EscalationPolicy TeamPolicy(Guid teamId, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "team-policy",
        IsActive = true,
        TeamId = teamId,
        CreatedAt = createdAt ?? DateTime.UtcNow.AddDays(-7),
        Steps = new List<EscalationStep> { Step(1, 0, "u1") }
    };

    /// <summary>
    /// An incident that IS marked as staged but never activated: the trigger message was lost.
    /// </summary>
    [Fact]
    public async Task Reconcile_ActivatesIncident_WhenPolicyExists_ButActivationNeverLanded()
    {
        var h = new Harness();
        var teamId = Guid.NewGuid();
        var policy = TeamPolicy(teamId);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "orphaned",
            Status = IncidentStatus.Open,
            TeamId = teamId,
            IsEscalationActive = false,
            EscalationPolicyId = null,
            EscalationStartedAt = null,
            CurrentEscalationStepId = null,
            EscalationStagedAt = DateTime.UtcNow.AddMinutes(-30),
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.True(updated.IsEscalationActive);
        Assert.Equal(policy.Id, updated.EscalationPolicyId);
    }

    /// <summary>A legacy incident with no staging marker is judged by its policy's age instead.</summary>
    [Fact]
    public async Task Reconcile_ActivatesLegacyIncidentWithoutMarker_WhenPolicyPredatesIt()
    {
        var h = new Harness();
        var teamId = Guid.NewGuid();
        var policy = TeamPolicy(teamId, createdAt: DateTime.UtcNow.AddDays(-3));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "created before the EscalationStagedAt column existed",
            Status = IncidentStatus.Open,
            TeamId = teamId,
            IsEscalationActive = false,
            EscalationPolicyId = null,
            EscalationStartedAt = null,
            CurrentEscalationStepId = null,
            EscalationStagedAt = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.True(updated.IsEscalationActive);
        Assert.Equal(policy.Id, updated.EscalationPolicyId);
    }

    [Fact]
    public async Task Reconcile_Skips_WhenEscalationWasNeverStaged()
    {
        var h = new Harness();
        var teamId = Guid.NewGuid();
        // The policy only appeared after the incident — nothing was ever staged for it, so the
        // incident must not be paged retroactively.
        var policy = TeamPolicy(teamId, createdAt: DateTime.UtcNow.AddMinutes(-10));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "created before the team had a policy",
            Status = IncidentStatus.Open,
            TeamId = teamId,
            IsEscalationActive = false,
            EscalationPolicyId = null,
            EscalationStartedAt = null,
            CurrentEscalationStepId = null,
            EscalationStagedAt = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.False(updated.IsEscalationActive);
        Assert.Null(updated.EscalationPolicyId);
    }

    /// <summary>A policy edited after the incident was created may not have been active then, so no lost trigger is assumed.</summary>
    [Fact]
    public async Task Reconcile_Skips_LegacyIncident_WhenPolicyWasTouchedAfterCreation()
    {
        var h = new Harness();
        var teamId = Guid.NewGuid();
        var policy = TeamPolicy(teamId, createdAt: DateTime.UtcNow.AddDays(-3));
        policy.UpdatedAt = DateTime.UtcNow.AddMinutes(-10);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "policy re-activated after the incident",
            Status = IncidentStatus.Open,
            TeamId = teamId,
            IsEscalationActive = false,
            EscalationStagedAt = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.False(updated.IsEscalationActive);
        Assert.Null(updated.EscalationPolicyId);
    }

    [Fact]
    public async Task Reconcile_Skips_WhenNoActivePolicyForTeam()
    {
        var h = new Harness();
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "orphaned-no-policy",
            Status = IncidentStatus.Open,
            TeamId = Guid.NewGuid(),
            IsEscalationActive = false,
            EscalationStagedAt = DateTime.UtcNow.AddMinutes(-30),
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        await h.SeedAsync(incident, policy: null);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.False(updated.IsEscalationActive);
        Assert.Null(updated.EscalationPolicyId);
    }

    /// <summary>
    /// An incident whose paging an alert rule deliberately suppressed is not a dead-lettered
    /// trigger, so the reconcile self-heal must never re-activate it.
    /// </summary>
    [Fact]
    public async Task Reconcile_Skips_WhenPagingSuppressed()
    {
        var h = new Harness();
        var teamId = Guid.NewGuid();
        var policy = TeamPolicy(teamId, createdAt: DateTime.UtcNow.AddDays(-3));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "suppressed-by-rule",
            Status = IncidentStatus.Open,
            TeamId = teamId,
            IsEscalationActive = false,
            IsPagingSuppressed = true,
            EscalationStagedAt = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.False(updated.IsEscalationActive);
        Assert.Null(updated.EscalationPolicyId);
    }

    [Fact]
    public async Task Reconcile_Skips_WithinGracePeriod()
    {
        var h = new Harness();
        var teamId = Guid.NewGuid();
        var policy = TeamPolicy(teamId);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "fresh",
            Status = IncidentStatus.Open,
            TeamId = teamId,
            IsEscalationActive = false,
            EscalationStagedAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.ProcessPendingEscalationsAsync();

        var updated = await h.ReloadAsync(incident.Id);
        Assert.False(updated.IsEscalationActive);
        Assert.Null(updated.EscalationPolicyId);
    }

    // ---- dispatch generation ----------------------------------------------

    /// <summary>The generation survives the microsecond truncation a Postgres round trip applies to the anchor.</summary>
    [Fact]
    public void DispatchGeneration_SurvivesTheMicrosecondRoundTripToPostgres()
    {
        var inMemory = new DateTime(2026, 7, 13, 9, 30, 15, DateTimeKind.Utc).AddTicks(1_234_567);
        var afterRoundTrip = new DateTime(
            inMemory.Ticks - (inMemory.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);

        Assert.NotEqual(inMemory.Ticks, afterRoundTrip.Ticks);
        Assert.Equal(
            EscalationOrchestrator.ComputeDispatchGeneration(inMemory),
            EscalationOrchestrator.ComputeDispatchGeneration(afterRoundTrip));
        Assert.Equal(afterRoundTrip.Ticks, EscalationOrchestrator.ComputeDispatchGeneration(inMemory));
    }

    [Fact]
    public void DispatchGeneration_IsZero_WhenEscalationNeverStarted()
    {
        Assert.Equal(0, EscalationOrchestrator.ComputeDispatchGeneration(null));
    }

    /// <summary>
    /// Two runs of the same incident (the reopen case) must not collide.
    /// </summary>
    [Fact]
    public void DispatchGeneration_Differs_BetweenRuns()
    {
        var firstRun = new DateTime(2026, 7, 13, 9, 30, 15, DateTimeKind.Utc);

        Assert.NotEqual(
            EscalationOrchestrator.ComputeDispatchGeneration(firstRun),
            EscalationOrchestrator.ComputeDispatchGeneration(firstRun.AddMinutes(20)));
    }

    // ---- CancelEscalationAsync ----------------------------------------------
    //
    // The one public method on IEscalationOrchestrator nothing calls yet. It is the interface's
    // answer to "stop paging people about this", so pin it now rather than during an incident.

    [Fact]
    public async Task Cancel_StopsTheSweepFromPagingAnyFurtherStep()
    {
        var h = new Harness();
        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1, Step(2, 0, "u2"));
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "DB unreachable",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = DateTime.UtcNow.AddMinutes(-10),
            CurrentEscalationStepId = step1.Id,
            LastEscalationStepAt = DateTime.UtcNow.AddMinutes(-10)
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.CancelEscalationAsync(incident.Id);

        Assert.False((await h.ReloadAsync(incident.Id)).IsEscalationActive);

        // ...and the sweep, whose predicate is exactly this flag, pages nobody afterwards.
        await h.Sut.ProcessPendingEscalationsAsync();

        await h.Dispatcher.DidNotReceiveWithAnyArgs()
            .NotifyUsersAsync(default!, default!, default);
    }

    /// <summary>Cancelling says stop, not forget: the run's step history is left intact.</summary>
    [Fact]
    public async Task Cancel_LeavesTheRunsHistoryIntact()
    {
        var h = new Harness();
        var step1 = Step(1, 0, "u1");
        var policy = Policy(step1);
        var startedAt = DateTime.UtcNow.AddMinutes(-10);
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "DB unreachable",
            Status = IncidentStatus.Open,
            IsEscalationActive = true,
            EscalationPolicyId = policy.Id,
            EscalationStartedAt = startedAt,
            CurrentEscalationStepId = step1.Id,
            LastEscalationStepAt = startedAt
        };
        await h.SeedAsync(incident, policy);

        await h.Sut.CancelEscalationAsync(incident.Id);

        var updated = await h.ReloadAsync(incident.Id);
        Assert.Equal(step1.Id, updated.CurrentEscalationStepId);
        Assert.Equal(policy.Id, updated.EscalationPolicyId);
        Assert.Equal(
            EscalationOrchestrator.ComputeDispatchGeneration(startedAt),
            EscalationOrchestrator.ComputeDispatchGeneration(updated.EscalationStartedAt));
    }

    /// <summary>An incident that is not there is not an error — a cancel that arrives late is a no-op.</summary>
    [Fact]
    public async Task Cancel_OnAnUnknownIncident_DoesNothing()
    {
        var h = new Harness();

        await h.Sut.CancelEscalationAsync(Guid.NewGuid());

        Assert.Empty(await h.Ctx.Incidents.ToListAsync());
    }
}
