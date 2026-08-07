using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Events;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Teams;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Pins the team-scoped half of the on-call membership cascade that removing a member has to run.</summary>
public class TeamServiceRemoveMemberTests : IDisposable
{
    private const string Target = "alice";
    private const string Bystander = "bob";

    private readonly ApplicationDbContext _ctx;
    private readonly ServiceProvider _sp;
    private readonly IScheduleMaterializer _materializer = Substitute.For<IScheduleMaterializer>();
    private readonly ICommunicationEventDispatcher _events = Substitute.For<ICommunicationEventDispatcher>();
    private readonly TeamService _sut;

    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Guid _otherTeamId = Guid.NewGuid();

    public TeamServiceRemoveMemberTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"remove-member-{Guid.NewGuid():N}").Options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        _sp = services.BuildServiceProvider();

        _sut = new TeamService(
            new TeamRepository(_ctx, NullLogger<TeamRepository>.Instance),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            new ScheduleRepository(_ctx, NullLogger<ScheduleRepository>.Instance),
            new ScheduleOccurrenceRepository(_ctx, NullLogger<ScheduleOccurrenceRepository>.Instance),
            new EscalationPolicyRepository(_ctx, NullLogger<EscalationPolicyRepository>.Instance),
            new EscalationStepRepository(_ctx, NullLogger<EscalationStepRepository>.Instance),
            new SavingTransactionManager(_ctx),
            MockUserManager(),
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IValidator<CreateTeamRequest>>(),
            _events,
            _materializer,
            _ctx,
            _sp.GetRequiredService<HybridCache>(),
            Substitute.For<IAuditLogService>(),
            NullLogger<TeamService>.Instance);
    }

    public void Dispose() { _ctx.Dispose(); _sp.Dispose(); }

    private static UserManager<ApplicationUser> MockUserManager() =>
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

    private Guid SeedMember(string userId, Guid teamId)
    {
        var member = new TeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            UserId = userId,
            Role = "Member",
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(member);
        _ctx.SaveChanges();
        return member.Id;
    }

    private Guid SeedSchedule(Guid teamId)
    {
        var schedule = new Schedule
        {
            Id = Guid.NewGuid(),
            Name = "Primary",
            TeamId = teamId,
            Timezone = "UTC",
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(schedule);
        _ctx.SaveChanges();
        return schedule.Id;
    }

    private Guid SeedRotation(string userId, Guid scheduleId)
    {
        var rotation = new ScheduleRotation
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            UserId = userId,
            HandoverStartLocal = new LocalDateTime(2026, 7, 6, 9, 0),
            ShiftLengthMinutes = 480,
            RecurrenceType = RecurrenceType.Weekly,
            IsPrimary = true,
            Order = 0,
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(rotation);
        _ctx.SaveChanges();
        return rotation.Id;
    }

    private void SeedOccurrence(string userId, Guid scheduleId, Guid rotationId)
    {
        _ctx.Add(new ScheduleOccurrence
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            RotationId = rotationId,
            UserId = userId,
            StartUtc = Instant.FromUtc(2026, 7, 13, 9, 0),
            EndUtc = Instant.FromUtc(2026, 7, 13, 17, 0),
            IsPrimary = true,
            Order = 0,
            MaterializedAt = Instant.FromUtc(2026, 7, 13, 0, 0),
            CreatedAt = DateTime.UtcNow
        });
        _ctx.SaveChanges();
    }

    /// <summary>Deleting a team is refused while another team's policy still pages it.</summary>
    [Fact]
    public async Task DeleteTeam_IsRefused_WhileAnotherTeamsPolicyPagesIt()
    {
        SeedTeamRow();
        var otherTeam = Guid.NewGuid();
        var policy = new EscalationPolicy
        {
            Id = Guid.NewGuid(),
            Name = "Platform ladder",
            TeamId = otherTeam,
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(policy);
        _ctx.Add(new EscalationStep
        {
            Id = Guid.NewGuid(),
            EscalationPolicyId = policy.Id,
            Level = 1,
            Title = "Escalate to the DB team",
            DelayMinutes = 5,
            TeamId = _teamId,
            CreatedAt = DateTime.UtcNow
        });
        await _ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<Callu.Shared.Exceptions.BusinessRuleException>(
            () => _sut.DeleteTeamAsync(_teamId));

        Assert.Contains("Platform ladder", ex.Message);

        var team = await _ctx.Teams.IgnoreQueryFilters().FirstAsync(x => x.Id == _teamId);
        Assert.False(team.IsDeleted);
    }

    /// <summary>The team's OWN policy is part of the same cleanup and must not block it.</summary>
    [Fact]
    public async Task DeleteTeam_IsAllowed_WhenOnlyItsOwnPolicyPagesIt()
    {
        SeedTeamRow();
        SeedEscalationTarget(Target, _teamId);
        await _ctx.SaveChangesAsync();

        await _sut.DeleteTeamAsync(_teamId);

        var team = await _ctx.Teams.IgnoreQueryFilters().FirstAsync(x => x.Id == _teamId);
        Assert.True(team.IsDeleted);
    }

    private void SeedTeamRow() => _ctx.Add(new Team
    {
        Id = _teamId,
        Name = "DB team",
        CreatedAt = DateTime.UtcNow
    });

    private void SeedEscalationTarget(string userId, Guid teamId)
    {
        var policy = new EscalationPolicy
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            TeamId = teamId,
            CreatedAt = DateTime.UtcNow
        };
        var step = new EscalationStep
        {
            Id = Guid.NewGuid(),
            EscalationPolicyId = policy.Id,
            Level = 1,
            Title = "Page primary",
            DelayMinutes = 0,
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(policy);
        _ctx.Add(step);
        _ctx.Add(new EscalationStepUser
        {
            EscalationStepId = step.Id,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        });
        _ctx.SaveChanges();
    }

    // ── The bug ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMember_SoftDeletesTheirRotationOnTheTeamsSchedule()
    {
        var memberId = SeedMember(Target, _teamId);
        var scheduleId = SeedSchedule(_teamId);
        SeedRotation(Target, scheduleId);

        await _sut.RemoveMemberAsync(_teamId, memberId);

        _ctx.ChangeTracker.Clear();
        // Left behind, the rotation kept expanding into occurrences the roster filter then dropped:
        // the schedule paged nobody, and said so only in a log line.
        Assert.Empty(await _ctx.ScheduleRotations.Where(r => r.UserId == Target).ToListAsync());
        Assert.All(
            await _ctx.ScheduleRotations.IgnoreQueryFilters().Where(r => r.UserId == Target).ToListAsync(),
            r => Assert.True(r.IsDeleted));
    }

    [Fact]
    public async Task RemoveMember_DropsTheirEscalationStepTargets_OnThisTeamsPolicies()
    {
        var memberId = SeedMember(Target, _teamId);
        SeedEscalationTarget(Target, _teamId);
        SeedEscalationTarget(Bystander, _teamId);

        await _sut.RemoveMemberAsync(_teamId, memberId);

        _ctx.ChangeTracker.Clear();
        var remaining = await _ctx.EscalationStepUsers.IgnoreQueryFilters().ToListAsync();
        Assert.Equal([Bystander], remaining.Select(e => e.UserId));
    }

    [Fact]
    public async Task RemoveMember_RematerializesTheAffectedSchedules()
    {
        var memberId = SeedMember(Target, _teamId);
        var scheduleId = SeedSchedule(_teamId);
        SeedRotation(Target, scheduleId);

        await _sut.RemoveMemberAsync(_teamId, memberId);

        await _materializer.Received(1).RematerializeScheduleAsync(
            scheduleId, IScheduleMaterializer.DefaultHorizon, Arg.Any<CancellationToken>());
    }

    /// <summary>The recovery flag is raised in the same transaction as the soft-delete.</summary>
    [Fact]
    public async Task RemoveMember_FlagsTheScheduleForRematerialize_InTheSameTransaction()
    {
        var memberId = SeedMember(Target, _teamId);
        var scheduleId = SeedSchedule(_teamId);
        SeedRotation(Target, scheduleId);

        await _sut.RemoveMemberAsync(_teamId, memberId);

        _ctx.ChangeTracker.Clear();
        var schedule = await _ctx.Schedules.SingleAsync(s => s.Id == scheduleId);
        Assert.NotNull(schedule.NeedsRematerializeSince);
    }

    /// <summary>
    /// Re-entrant: a removal whose rematerialize never landed leaves occurrences still naming the
    /// user, and re-issuing the removal has to pick them up even though the rotation is already gone.
    /// </summary>
    [Fact]
    public async Task RemoveMember_RepairsAScheduleWhoseOccurrencesStillNameThem()
    {
        var memberId = SeedMember(Target, _teamId);
        var scheduleId = SeedSchedule(_teamId);
        var rotationId = SeedRotation(Target, scheduleId);
        SeedOccurrence(Target, scheduleId, rotationId);

        // First attempt soft-deletes the rotation but (pretend) dies before rematerializing.
        await _sut.RemoveMemberAsync(_teamId, memberId);
        _materializer.ClearReceivedCalls();

        // The stale occurrence is still there; a second removal of the same person must still repair.
        var secondMemberId = SeedMember(Target, _teamId);
        await _sut.RemoveMemberAsync(_teamId, secondMemberId);

        await _materializer.Received(1).RematerializeScheduleAsync(
            scheduleId, IScheduleMaterializer.DefaultHorizon, Arg.Any<CancellationToken>());
    }

    // ── Blast radius ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMember_LeavesOtherMembersRotationsAlone()
    {
        var memberId = SeedMember(Target, _teamId);
        SeedMember(Bystander, _teamId);
        var scheduleId = SeedSchedule(_teamId);
        SeedRotation(Target, scheduleId);
        SeedRotation(Bystander, scheduleId);

        await _sut.RemoveMemberAsync(_teamId, memberId);

        _ctx.ChangeTracker.Clear();
        var rotations = await _ctx.ScheduleRotations.Where(r => r.ScheduleId == scheduleId).ToListAsync();
        Assert.Equal([Bystander], rotations.Select(r => r.UserId));
    }

    /// <summary>
    /// Scoped to the team being left. The person is still an active member of the other team, and
    /// taking them off ITS rota would page nobody there — the very bug, inverted.
    /// </summary>
    [Fact]
    public async Task RemoveMember_LeavesTheirRotationsOnOtherTeamsAlone()
    {
        var memberId = SeedMember(Target, _teamId);
        SeedMember(Target, _otherTeamId);

        var thisSchedule = SeedSchedule(_teamId);
        var otherSchedule = SeedSchedule(_otherTeamId);
        SeedRotation(Target, thisSchedule);
        SeedRotation(Target, otherSchedule);

        SeedEscalationTarget(Target, _otherTeamId);

        await _sut.RemoveMemberAsync(_teamId, memberId);

        _ctx.ChangeTracker.Clear();
        var live = await _ctx.ScheduleRotations.Where(r => r.UserId == Target).ToListAsync();
        Assert.Equal([otherSchedule], live.Select(r => r.ScheduleId));

        // And the other team's escalation policy still names them.
        Assert.Single(await _ctx.EscalationStepUsers.IgnoreQueryFilters().Where(e => e.UserId == Target).ToListAsync());

        await _materializer.DidNotReceive().RematerializeScheduleAsync(
            otherSchedule, Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveMember_StillAnnouncesTheRemoval()
    {
        var memberId = SeedMember(Target, _teamId);

        await _sut.RemoveMemberAsync(_teamId, memberId);

        await _events.Received(1).DispatchAsync(
            Arg.Is<TeamMemberRemovedEvent>(e => e.UserId == Target && e.TeamId == _teamId),
            Arg.Any<CancellationToken>());
    }
}
