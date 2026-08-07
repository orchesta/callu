using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Removing a user clears the whole on-call surface: memberships, rotations, escalation targets and occurrences.</summary>
public class UserManagementRemoveUserTests : IDisposable
{
    private const string Target = "alice";
    private const string Bystander = "bob";

    private readonly ApplicationDbContext _ctx;
    private readonly ServiceProvider _sp;
    private readonly IScheduleMaterializer _materializer = Substitute.For<IScheduleMaterializer>();
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserManagementService _sut;

    public UserManagementRemoveUserTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"remove-user-{Guid.NewGuid():N}").Options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        _sp = services.BuildServiceProvider();

        _userManager = MockUserManager(User(Target), User(Bystander));

        _sut = new UserManagementService(
            _userManager,
            MockRoleManager(),
            Substitute.For<IEmailService>(),
            new SavingTransactionManager(_ctx),
            Substitute.For<ITenantUserReadRepository>(),
            Substitute.For<IOrganizationSettingsService>(),
            Substitute.For<IRefreshTokenRepository>(),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            _materializer,
            _ctx,
            _sp.GetRequiredService<HybridCache>(),
            Substitute.For<IAuditLogService>(),
            Substitute.For<ICurrentUserService>(),
            NullLogger<UserManagementService>.Instance);
    }

    public void Dispose() { _ctx.Dispose(); _sp.Dispose(); }

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        FirstName = id,
        LastName = "Tester",
        Email = $"{id}@example.io",
        UserName = $"{id}@example.io"
    };

    private static UserManager<ApplicationUser> MockUserManager(params ApplicationUser[] users)
    {
        var mgr = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        foreach (var u in users)
            mgr.FindByIdAsync(u.Id).Returns(u);
        mgr.UpdateAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        mgr.UpdateSecurityStampAsync(Arg.Any<ApplicationUser>()).Returns(IdentityResult.Success);
        return mgr;
    }

    private static RoleManager<ApplicationRole> MockRoleManager() =>
        Substitute.For<RoleManager<ApplicationRole>>(
            Substitute.For<IRoleStore<ApplicationRole>>(), null, null, null, null);

    private Guid SeedMembership(string userId, Guid? teamId = null)
    {
        var team = teamId ?? Guid.NewGuid();
        _ctx.Add(new TeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = team,
            UserId = userId,
            Role = "Member",
            CreatedAt = DateTime.UtcNow
        });
        _ctx.SaveChanges();
        return team;
    }

    private Guid SeedRotation(string userId, Guid? scheduleId = null)
    {
        var schedule = scheduleId ?? Guid.NewGuid();
        _ctx.Add(new ScheduleRotation
        {
            Id = Guid.NewGuid(),
            ScheduleId = schedule,
            UserId = userId,
            HandoverStartLocal = new LocalDateTime(2026, 7, 6, 9, 0),
            ShiftLengthMinutes = 480,
            RecurrenceType = RecurrenceType.Weekly,
            IsPrimary = true,
            Order = 0,
            CreatedAt = DateTime.UtcNow
        });
        _ctx.SaveChanges();
        return schedule;
    }

    private void SeedEscalationTarget(string userId)
    {
        _ctx.Add(new EscalationStepUser
        {
            EscalationStepId = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        });
        _ctx.SaveChanges();
    }

    [Fact]
    public async Task RemoveUser_SoftDeletesTeamMembershipsAndRotations()
    {
        SeedMembership(Target);
        var scheduleId = SeedRotation(Target);

        Assert.True(await _sut.RemoveUserAsync(Target));

        _ctx.ChangeTracker.Clear();
        Assert.Empty(await _ctx.TeamMembers.Where(m => m.UserId == Target).ToListAsync());
        Assert.Empty(await _ctx.ScheduleRotations.Where(r => r.ScheduleId == scheduleId).ToListAsync());

        // The global soft-delete filter hides them; the rows themselves are flagged, not dropped.
        Assert.All(
            await _ctx.TeamMembers.IgnoreQueryFilters().Where(m => m.UserId == Target).ToListAsync(),
            m => Assert.True(m.IsDeleted));
        Assert.All(
            await _ctx.ScheduleRotations.IgnoreQueryFilters().Where(r => r.UserId == Target).ToListAsync(),
            r => Assert.True(r.IsDeleted));
    }

    [Fact]
    public async Task RemoveUser_HardDeletesEscalationStepTargets()
    {
        SeedEscalationTarget(Target);
        SeedEscalationTarget(Bystander);

        Assert.True(await _sut.RemoveUserAsync(Target));

        _ctx.ChangeTracker.Clear();
        var remaining = await _ctx.EscalationStepUsers.IgnoreQueryFilters().ToListAsync();
        Assert.Equal([Bystander], remaining.Select(e => e.UserId));
    }

    [Fact]
    public async Task RemoveUser_RematerializesEveryAffectedSchedule()
    {
        var scheduleA = SeedRotation(Target);
        var scheduleB = SeedRotation(Target);

        Assert.True(await _sut.RemoveUserAsync(Target));

        await _materializer.Received(1).RematerializeScheduleAsync(
            scheduleA, IScheduleMaterializer.DefaultHorizon, Arg.Any<CancellationToken>());
        await _materializer.Received(1).RematerializeScheduleAsync(
            scheduleB, IScheduleMaterializer.DefaultHorizon, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveUser_LeavesOtherMembersAlone()
    {
        var teamId = SeedMembership(Target);
        SeedMembership(Bystander, teamId);
        var scheduleId = SeedRotation(Target);
        SeedRotation(Bystander, scheduleId);

        Assert.True(await _sut.RemoveUserAsync(Target));

        _ctx.ChangeTracker.Clear();
        var members = await _ctx.TeamMembers.Where(m => m.TeamId == teamId).ToListAsync();
        Assert.Equal([Bystander], members.Select(m => m.UserId));

        var rotations = await _ctx.ScheduleRotations.Where(r => r.ScheduleId == scheduleId).ToListAsync();
        Assert.Equal([Bystander], rotations.Select(r => r.UserId));
    }

    [Fact]
    public async Task RemoveUser_IsIdempotent_OnAnAlreadyRemovedAccount()
    {
        SeedRotation(Target);
        Assert.True(await _sut.RemoveUserAsync(Target));

        _materializer.ClearReceivedCalls();

        Assert.False(await _sut.RemoveUserAsync(Target));
        await _materializer.DidNotReceive().RematerializeScheduleAsync(
            Arg.Any<Guid>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>());
    }
}
