using Callu.Application.Services;
using Callu.Application.Validators;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Schedules;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Callu.Tests;

/// <summary>The batch plan endpoint writes the whole rota in one transaction and rematerializes exactly once.</summary>
public class ScheduleServicePlanTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly ServiceProvider _sp;
    private readonly IScheduleMaterializer _materializer = Substitute.For<IScheduleMaterializer>();
    private readonly ScheduleService _sut;

    private readonly Guid _scheduleId = Guid.NewGuid();
    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Guid _otherTeamId = Guid.NewGuid();
    private readonly Guid _aliceRotationId = Guid.NewGuid();
    private readonly Guid _bobRotationId = Guid.NewGuid();

    private static readonly LocalDateTime AliceHandover = new(2026, 7, 6, 0, 0);
    private static readonly LocalDateTime BobHandover = new(2026, 7, 13, 0, 0);

    public ScheduleServicePlanTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"plan-{Guid.NewGuid():N}").Options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        _sp = services.BuildServiceProvider();

        var tz = DateTimeZoneProviders.Tzdb;

        _sut = new ScheduleService(
            new ScheduleRepository(_ctx, NullLogger<ScheduleRepository>.Instance),
            new ScheduleOccurrenceRepository(_ctx, NullLogger<ScheduleOccurrenceRepository>.Instance),
            new ScheduleRotationRepository(_ctx, NullLogger<ScheduleRotationRepository>.Instance),
            new EscalationStepRepository(_ctx, NullLogger<EscalationStepRepository>.Instance),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            new SavingTransactionManager(_ctx),
            MockUserManager("alice", "bob", "carol"),
            new CreateScheduleRequestValidator(tz),
            new SaveSchedulePlanRequestValidator(tz),
            _materializer,
            tz,
            Substitute.For<IOnCallOverrideService>(),
            Substitute.For<IAuditLogService>(),
            _sp.GetRequiredService<HybridCache>(),
            NullLogger<ScheduleService>.Instance,
            Substitute.For<IOnCallService>());

        _ctx.Add(new Schedule
        {
            Id = _scheduleId,
            Name = "Primary",
            Description = "Original",
            TeamId = _teamId,
            Timezone = "Europe/Istanbul",
            CreatedAt = DateTime.UtcNow
        });
        _ctx.Add(Rotation(_aliceRotationId, "alice", AliceHandover, isPrimary: true, order: 1));
        _ctx.Add(Rotation(_bobRotationId, "bob", BobHandover, isPrimary: false, order: 2));

        // alice and bob staff the owning team; carol is only on the other one.
        SeedMember(_teamId, "alice");
        SeedMember(_teamId, "bob");
        SeedMember(_otherTeamId, "carol");
        _ctx.SaveChanges();
    }

    public void Dispose() { _ctx.Dispose(); _sp.Dispose(); }

    // ---- the bug the endpoint exists for -------------------------------------------------

    [Fact]
    public async Task SavePlan_Reorder_RematerializesOnce()
    {
        // Three PUTs would have rematerialized three times, publishing two rotas nobody authored.
        var saved = await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
        {
            Rotations = [PlanRotation(_bobRotationId, "bob", AliceHandover, true, 1),
                         PlanRotation(_aliceRotationId, "alice", BobHandover, false, 2)]
        });

        Assert.True(saved);
        await _materializer.Received(1).RematerializeScheduleAsync(
            _scheduleId, Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePlan_Reorder_TakesEffect()
    {
        // H05: swapping the member order has to actually move who is on call when.
        await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
        {
            Rotations = [PlanRotation(_bobRotationId, "bob", AliceHandover, true, 1),
                         PlanRotation(_aliceRotationId, "alice", BobHandover, false, 2)]
        });

        var bob = await Stored(_bobRotationId);
        var alice = await Stored(_aliceRotationId);

        Assert.Equal(AliceHandover, bob.HandoverStartLocal);
        Assert.True(bob.IsPrimary);
        Assert.Equal(1, bob.Order);
        Assert.Equal(BobHandover, alice.HandoverStartLocal);
        Assert.False(alice.IsPrimary);
        Assert.Equal(2, alice.Order);
    }

    // ---- the invariant a rewrite must not break -------------------------------------------

    [Fact]
    public async Task SavePlan_WithoutRotations_LeavesEveryHandoverAlone()
    {
        // An edit to the name/description alone: the SPA's timing fields are derived from
        // rotation #1 only, so rewriting the rota from them would silently move live on-call weeks.
        await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
        {
            Name = "Renamed",
            Description = "Now with a description"
        });

        var alice = await Stored(_aliceRotationId);
        var bob = await Stored(_bobRotationId);

        Assert.Equal(AliceHandover, alice.HandoverStartLocal);
        Assert.Equal(BobHandover, bob.HandoverStartLocal);
        Assert.True(alice.IsPrimary);
        Assert.Equal(2, bob.Order);

        // Names do not move any shift, so there is nothing to regenerate.
        await _materializer.DidNotReceive().RematerializeScheduleAsync(
            Arg.Any<Guid>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePlan_RestatingTheStoredRota_WritesNothing()
    {
        await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
        {
            Name = "Primary",
            Rotations = [PlanRotation(_aliceRotationId, "alice", AliceHandover, true, 1),
                         PlanRotation(_bobRotationId, "bob", BobHandover, false, 2)]
        });

        await _materializer.DidNotReceive().RematerializeScheduleAsync(
            Arg.Any<Guid>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    // ---- team moves ------------------------------------------------------------------------

    [Fact]
    public async Task SavePlan_TeamMoveWithAMatchingRota_IsAccepted()
    {
        // The guard used to read the STORED rotations, so a schedule's team could never be
        // changed: the members it was moving away from always failed the new team's roster check.
        var saved = await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
        {
            TeamId = _otherTeamId,
            Rotations = [PlanRotation(null, "carol", AliceHandover, true, 1)]
        });

        Assert.True(saved);

        var schedule = await _ctx.Schedules.AsNoTracking().SingleAsync(s => s.Id == _scheduleId);
        Assert.Equal(_otherTeamId, schedule.TeamId);

        var live = await _ctx.ScheduleRotations.AsNoTracking()
            .Where(r => r.ScheduleId == _scheduleId && !r.IsDeleted)
            .ToListAsync();
        Assert.Equal("carol", Assert.Single(live).UserId);

        await _materializer.Received(1).RematerializeScheduleAsync(
            _scheduleId, Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePlan_TeamMoveThatStrandsTheRota_IsRejectedAndWritesNothing()
    {
        // On-call reads filter occurrences by the team roster, so alice and bob would be dropped
        // and the schedule would page nobody. Refuse rather than accept and silently do nothing.
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
            {
                TeamId = _otherTeamId
            }));

        var schedule = await _ctx.Schedules.AsNoTracking().SingleAsync(s => s.Id == _scheduleId);
        Assert.Equal(_teamId, schedule.TeamId);

        await _materializer.DidNotReceive().RematerializeScheduleAsync(
            Arg.Any<Guid>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePlan_RotationForAnOffTeamMember_IsRejected()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
            {
                Rotations = [PlanRotation(_aliceRotationId, "alice", AliceHandover, true, 1),
                             PlanRotation(null, "carol", BobHandover, false, 2)]
            }));

        var live = await _ctx.ScheduleRotations.AsNoTracking()
            .Where(r => r.ScheduleId == _scheduleId && !r.IsDeleted)
            .CountAsync();
        Assert.Equal(2, live);
    }

    // ---- the rest of the plan ----------------------------------------------------------------

    [Fact]
    public async Task SavePlan_TimezoneChangeAlone_RematerializesAndKeepsTheRota()
    {
        await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
        {
            Timezone = "Europe/London"
        });

        var schedule = await _ctx.Schedules.AsNoTracking().SingleAsync(s => s.Id == _scheduleId);
        Assert.Equal("Europe/London", schedule.Timezone);

        // The wall-clock handovers stay put; their UTC instants are what moves, which is exactly
        // what the rematerialize recomputes.
        Assert.Equal(AliceHandover, (await Stored(_aliceRotationId)).HandoverStartLocal);
        await _materializer.Received(1).RematerializeScheduleAsync(
            _scheduleId, Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePlan_UnknownTimezone_IsRejected()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
            {
                Timezone = "Mars/Olympus"
            }));

        var schedule = await _ctx.Schedules.AsNoTracking().SingleAsync(s => s.Id == _scheduleId);
        Assert.Equal("Europe/Istanbul", schedule.Timezone);
    }

    [Fact]
    public async Task SavePlan_MemberLeftOutOfTheList_IsRemoved()
    {
        await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
        {
            Rotations = [PlanRotation(_aliceRotationId, "alice", AliceHandover, true, 1)]
        });

        var bob = await _ctx.ScheduleRotations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(r => r.Id == _bobRotationId);
        Assert.True(bob.IsDeleted);

        await _materializer.Received(1).RematerializeScheduleAsync(
            _scheduleId, Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePlan_DuplicateMember_IsRejected()
    {
        // Two rotations for one person pages them twice per cycle.
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
            {
                Rotations = [PlanRotation(_aliceRotationId, "alice", AliceHandover, true, 1),
                             PlanRotation(null, "alice", BobHandover, false, 2)]
            }));
    }

    [Fact]
    public async Task SavePlan_EmptyRotationList_IsRejectedAndLeavesTheRotaStanding()
    {
        // `rotations` is a full replacement, so [] means "soft-delete every rotation" — the schedule
        // survives with nobody on it and an incident routed to it pages NOBODY. The SPA cannot ask
        // for this; the API contract could.
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
            {
                Rotations = []
            }));

        var live = await _ctx.ScheduleRotations.AsNoTracking()
            .Where(r => r.ScheduleId == _scheduleId && !r.IsDeleted)
            .CountAsync();
        Assert.Equal(2, live);

        await _materializer.DidNotReceive().RematerializeScheduleAsync(
            Arg.Any<Guid>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    // ---- surviving a rematerialize that never lands -------------------------------------------

    [Fact]
    public async Task SavePlan_RematerializeFails_CommitsThePlanAndRecordsThatTheRotaIsBehindIt()
    {
        _materializer
            .RematerializeScheduleAsync(_scheduleId, Arg.Any<Duration>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the occurrence table is unreachable"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SaveSchedulePlanAsync(_scheduleId, SwappedRota));

        // The plan committed in its own transaction and is durable...
        Assert.Equal(AliceHandover, (await Stored(_bobRotationId)).HandoverStartLocal);

        // ...so the only thing standing between the operator and a rota that expands the PREVIOUS
        // plan is this flag. It has to have been written in that same transaction.
        var schedule = await _ctx.Schedules.AsNoTracking().SingleAsync(s => s.Id == _scheduleId);
        Assert.NotNull(schedule.NeedsRematerializeSince);
    }

    [Fact]
    public async Task SavePlan_RestatingAPlanWhoseRematerializeNeverLanded_RegeneratesRatherThanReportingSuccess()
    {
        // The state a crashed post-commit rematerialize leaves behind: the plan is stored, the
        // occurrence table still expands the one before it.
        var stale = await _ctx.Schedules.SingleAsync(s => s.Id == _scheduleId);
        stale.NeedsRematerializeSince = DateTime.UtcNow.AddMinutes(-5);
        await _ctx.SaveChangesAsync();

        // Field by field this restates exactly what is stored, so the no-op shortcut used to fire:
        // "200 OK", no rematerialize, and the wrong member kept the pager until the 03:00 job.
        Assert.True(await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
        {
            Rotations = [PlanRotation(_aliceRotationId, "alice", AliceHandover, true, 1),
                         PlanRotation(_bobRotationId, "bob", BobHandover, false, 2)]
        }));

        await _materializer.Received(1).RematerializeScheduleAsync(
            _scheduleId, Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePlan_UnrelatedEditOnAScheduleWhoseRotaIsBehind_AlsoRecoversIt()
    {
        var stale = await _ctx.Schedules.SingleAsync(s => s.Id == _scheduleId);
        stale.NeedsRematerializeSince = DateTime.UtcNow.AddMinutes(-5);
        await _ctx.SaveChangesAsync();

        // A rename sends no rotations and normally regenerates nothing. On a schedule that is known
        // to owe a rematerialize it must still run one — and it still may not move a handover.
        await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest { Name = "Renamed" });

        Assert.Equal(AliceHandover, (await Stored(_aliceRotationId)).HandoverStartLocal);
        Assert.Equal(BobHandover, (await Stored(_bobRotationId)).HandoverStartLocal);

        await _materializer.Received(1).RematerializeScheduleAsync(
            _scheduleId, Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavePlan_MissingSchedule_ReturnsFalse()
    {
        Assert.False(await _sut.SaveSchedulePlanAsync(Guid.NewGuid(), new SaveSchedulePlanRequest
        {
            Name = "Nowhere"
        }));
    }

    [Fact]
    public async Task SavePlan_SwitchingToTwentyFourSeven_ClearsOwnershipDays()
    {
        // The per-rotation PUT is a patch, so a stored OwnershipDays could never be cleared and a
        // partial-day schedule kept its per-day blocks after switching to 24/7. The plan replaces.
        var alice = await Stored(_aliceRotationId);
        alice.OwnershipDays = 7;
        alice.ShiftLengthMinutes = 480;
        await _ctx.SaveChangesAsync();

        await _sut.SaveSchedulePlanAsync(_scheduleId, new SaveSchedulePlanRequest
        {
            Rotations = [PlanRotation(_aliceRotationId, "alice", AliceHandover, true, 1, ownershipDays: null, shiftLengthMinutes: 7 * 1440)]
        });

        var updated = await Stored(_aliceRotationId);
        Assert.Null(updated.OwnershipDays);
        Assert.Equal(7 * 1440, updated.ShiftLengthMinutes);
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>alice and bob trade places: bob takes the shift alice is holding.</summary>
    private SaveSchedulePlanRequest SwappedRota => new()
    {
        Rotations = [PlanRotation(_bobRotationId, "bob", AliceHandover, true, 1),
                     PlanRotation(_aliceRotationId, "alice", BobHandover, false, 2)]
    };

    private async Task<ScheduleRotation> Stored(Guid rotationId) =>
        await _ctx.ScheduleRotations.AsNoTracking().SingleAsync(r => r.Id == rotationId);

    private ScheduleRotation Rotation(
        Guid id, string userId, LocalDateTime handover, bool isPrimary, int order) => new()
    {
        Id = id,
        ScheduleId = _scheduleId,
        UserId = userId,
        HandoverStartLocal = handover,
        ShiftLengthMinutes = 7 * 1440,
        IsPrimary = isPrimary,
        Order = order,
        RecurrenceType = RecurrenceType.Biweekly,
        RecurrenceIntervalDays = 14,
        CreatedAt = DateTime.UtcNow
    };

    private static SchedulePlanRotation PlanRotation(
        Guid? id, string userId, LocalDateTime handover, bool isPrimary, int order,
        int? ownershipDays = null, int shiftLengthMinutes = 7 * 1440) => new()
    {
        Id = id,
        UserId = userId,
        HandoverStartLocal = handover,
        ShiftLengthMinutes = shiftLengthMinutes,
        IsPrimary = isPrimary,
        Order = order,
        RecurrenceType = RecurrenceType.Biweekly,
        RecurrenceIntervalDays = 14,
        OwnershipDays = ownershipDays
    };

    private void SeedMember(Guid teamId, string userId) =>
        _ctx.Add(new TeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            UserId = userId,
            Role = "Member",
            CreatedAt = DateTime.UtcNow
        });

    private static UserManager<ApplicationUser> MockUserManager(params string[] userIds)
    {
        var mgr = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        foreach (var id in userIds)
            mgr.FindByIdAsync(id).Returns(new ApplicationUser
            {
                Id = id,
                FirstName = id,
                LastName = "Tester",
                Email = $"{id}@example.io"
            });
        return mgr;
    }
    /// <summary>Deleting a schedule a live escalation step still names would leave it paging nobody.</summary>
    [Fact]
    public async Task DeleteSchedule_IsRefused_WhileAnEscalationStepStillNamesIt()
    {
        var policy = new EscalationPolicy
        {
            Id = Guid.NewGuid(),
            Name = "Night ladder",
            TeamId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(policy);
        _ctx.Add(new EscalationStep
        {
            Id = Guid.NewGuid(),
            EscalationPolicyId = policy.Id,
            Level = 1,
            Title = "Page the rota",
            DelayMinutes = 0,
            ScheduleId = _scheduleId,
            CreatedAt = DateTime.UtcNow
        });
        await _ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<Callu.Shared.Exceptions.BusinessRuleException>(
            () => _sut.DeleteScheduleAsync(_scheduleId));

        Assert.Contains("Night ladder", ex.Message);

        var schedule = await _ctx.Schedules.IgnoreQueryFilters().FirstAsync(s => s.Id == _scheduleId);
        Assert.False(schedule.IsDeleted);
    }

    /// <summary>A step whose policy is already deleted is not a live paging target and must not block.</summary>
    [Fact]
    public async Task DeleteSchedule_IsAllowed_WhenTheReferencingPolicyIsDeleted()
    {
        var policy = new EscalationPolicy
        {
            Id = Guid.NewGuid(),
            Name = "Retired ladder",
            TeamId = Guid.NewGuid(),
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Add(policy);
        _ctx.Add(new EscalationStep
        {
            Id = Guid.NewGuid(),
            EscalationPolicyId = policy.Id,
            Level = 1,
            Title = "Page the rota",
            DelayMinutes = 0,
            ScheduleId = _scheduleId,
            CreatedAt = DateTime.UtcNow
        });
        await _ctx.SaveChangesAsync();

        // Asserts the GUARD's verdict, not the delete's completion: the in-memory provider cannot run
        // the ExecuteDeleteAsync that clears the occurrences afterwards, so the call still throws —
        // just not the refusal. The full delete is covered against real Postgres elsewhere.
        var ex = await Record.ExceptionAsync(() => _sut.DeleteScheduleAsync(_scheduleId));

        Assert.IsNotType<Callu.Shared.Exceptions.BusinessRuleException>(ex);
    }

}
