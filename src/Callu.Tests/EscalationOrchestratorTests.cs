using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

public class EscalationOrchestratorTests
{
    [Fact]
    public void ResolveTarget_PrefersUsers_OverScheduleAndTeam()
    {
        var step = new EscalationStep
        {
            TargetedUsers = new List<EscalationStepUser> { new() { UserId = "u1" } },
            ScheduleId = Guid.NewGuid(),
            TeamId = Guid.NewGuid()
        };

        var target = EscalationOrchestrator.ResolveTarget(step);

        Assert.Equal(EscalationOrchestrator.EscalationTargetKind.Users, target.Kind);
        Assert.Equal(new[] { "u1" }, target.UserIds);
    }

    [Fact]
    public void ResolveTarget_FallsBackToSchedule_WhenNoUsers()
    {
        var scheduleId = Guid.NewGuid();
        var target = EscalationOrchestrator.ResolveTarget(new EscalationStep { ScheduleId = scheduleId, TeamId = Guid.NewGuid() });

        Assert.Equal(EscalationOrchestrator.EscalationTargetKind.Schedule, target.Kind);
        Assert.Equal(scheduleId, target.ScheduleId);
    }

    [Fact]
    public void ResolveTarget_FallsBackToTeam_WhenNoUsersOrSchedule()
    {
        var teamId = Guid.NewGuid();
        var target = EscalationOrchestrator.ResolveTarget(new EscalationStep { TeamId = teamId, NotifyAllTeamMembers = true });

        Assert.Equal(EscalationOrchestrator.EscalationTargetKind.Team, target.Kind);
        Assert.Equal(teamId, target.TeamId);
        Assert.True(target.NotifyAllTeamMembers);
    }

    [Fact]
    public void ResolveTarget_ReturnsNone_WhenNothingConfigured()
    {
        Assert.Equal(EscalationOrchestrator.EscalationTargetKind.None,
            EscalationOrchestrator.ResolveTarget(new EscalationStep()).Kind);
    }

    [Fact]
    public void ResolveTarget_IgnoresBlankUserIds_AndFallsBack()
    {
        var step = new EscalationStep
        {
            TargetedUsers = new List<EscalationStepUser> { new() { UserId = "  " } },
            ScheduleId = Guid.NewGuid()
        };

        Assert.Equal(EscalationOrchestrator.EscalationTargetKind.Schedule,
            EscalationOrchestrator.ResolveTarget(step).Kind);
    }

    [Fact]
    public async Task DispatchTarget_WritesNobodyPagedTimeline_WhenNobodyReached()
    {
        var dispatcher = Substitute.For<INotificationDispatcher>();
        dispatcher.NotifyTeamAsync(Arg.Any<Guid>(), Arg.Any<NotificationPayload>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var timelineRepo = Substitute.For<IIncidentTimelineEventRepository>();
        var auditLog = Substitute.For<IAuditLogService>();
        var sut = CreateOrchestrator(dispatcher, timelineRepo, auditLog);

        await sut.DispatchTargetAsync(TeamPlan(level: 2), CancellationToken.None);

        await timelineRepo.Received(1).AddAsync(
            Arg.Is<IncidentTimelineEvent>(e => e.Title.Contains("nobody paged")),
            Arg.Any<CancellationToken>());
        await auditLog.Received(1).LogAsync(
            Arg.Any<string?>(), "EscalationNobodyReached", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchTarget_DoesNotWriteTimeline_WhenSomeoneReached()
    {
        var dispatcher = Substitute.For<INotificationDispatcher>();
        dispatcher.NotifyTeamAsync(Arg.Any<Guid>(), Arg.Any<NotificationPayload>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var timelineRepo = Substitute.For<IIncidentTimelineEventRepository>();
        var sut = CreateOrchestrator(dispatcher, timelineRepo, Substitute.For<IAuditLogService>());

        await sut.DispatchTargetAsync(TeamPlan(level: 1), CancellationToken.None);

        await timelineRepo.DidNotReceive().AddAsync(Arg.Any<IncidentTimelineEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchTarget_WritesTimeline_WhenStepHasNoTargetConfigured()
    {
        var dispatcher = Substitute.For<INotificationDispatcher>();
        var timelineRepo = Substitute.For<IIncidentTimelineEventRepository>();
        var sut = CreateOrchestrator(dispatcher, timelineRepo, Substitute.For<IAuditLogService>());

        var plan = new EscalationOrchestrator.EscalationPlan(
            Guid.NewGuid(),
            new EscalationStep { Level = 3 },
            BuildPayload(),
            new EscalationOrchestrator.EscalationTarget(EscalationOrchestrator.EscalationTargetKind.None));

        await sut.DispatchTargetAsync(plan, CancellationToken.None);

        await dispatcher.DidNotReceive().NotifyUsersAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>());
        await timelineRepo.Received(1).AddAsync(
            Arg.Is<IncidentTimelineEvent>(e => e.Title.Contains("nobody paged")),
            Arg.Any<CancellationToken>());
    }

    private static EscalationOrchestrator CreateOrchestrator(
        INotificationDispatcher dispatcher,
        IIncidentTimelineEventRepository timelineRepo,
        IAuditLogService auditLog) =>
        new(
            Substitute.For<IIncidentRepository>(),
            timelineRepo,
            auditLog,
            new ImmediateTransactionManager(),
            dispatcher,
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<EscalationOrchestrator>.Instance);

    private static NotificationPayload BuildPayload(int level = 1) => new()
    {
        IncidentId = Guid.NewGuid(),
        Title = "Database unreachable",
        Severity = "High",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = level
    };

    private static EscalationOrchestrator.EscalationPlan TeamPlan(int level) => new(
        Guid.NewGuid(),
        new EscalationStep { Level = level },
        BuildPayload(level),
        new EscalationOrchestrator.EscalationTarget(
            EscalationOrchestrator.EscalationTargetKind.Team,
            TeamId: Guid.NewGuid(),
            NotifyAllTeamMembers: true));
}
