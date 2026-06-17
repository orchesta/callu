using Callu.Domain.Entities;
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

/// <summary>
/// Integration-style coverage of on-call resolution over EF in-memory with real repositories:
/// occurrence range scan, the team-roster drift guard (departed members dropped), override
/// precedence (replaces both slots, suppressed when off-roster), and the nobody-on-call cases.
/// HybridCache is a real in-memory instance; UserManager (display names) and IClock are mocked.
/// </summary>
public class OnCallServiceTests : IDisposable
{
    private static readonly Instant Now = Instant.FromUtc(2026, 6, 7, 12, 0);

    private readonly ApplicationDbContext _ctx;
    private readonly ServiceProvider _sp;
    private readonly OnCallService _sut;
    private readonly UserManager<ApplicationUser> _userManager;

    public OnCallServiceTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"oncall-{Guid.NewGuid():N}").Options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        _sp = services.BuildServiceProvider();

        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        _userManager = MockUserManager(
            User("alice"), User("bob"), User("charlie"), User("dave"));

        _sut = new OnCallService(
            new ScheduleRepository(_ctx, NullLogger<ScheduleRepository>.Instance),
            new ScheduleOccurrenceRepository(_ctx, NullLogger<ScheduleOccurrenceRepository>.Instance),
            new OnCallOverrideRepository(_ctx, NullLogger<OnCallOverrideRepository>.Instance),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            new SavingTransactionManager(_ctx),
            _userManager,
            clock,
            _sp.GetRequiredService<HybridCache>(),
            NullLogger<OnCallService>.Instance);
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
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var mgr = Substitute.For<UserManager<ApplicationUser>>(
            store, null, null, null, null, null, null, null, null);
        foreach (var u in users)
            mgr.FindByIdAsync(u.Id).Returns(u);
        return mgr;
    }

    private Guid SeedSchedule(Guid teamId, string name = "Primary")
    {
        var id = Guid.NewGuid();
        _ctx.Add(new Schedule { Id = id, Name = name, TeamId = teamId, Timezone = "UTC", IsDeleted = false, CreatedAt = DateTime.UtcNow });
        return id;
    }

    private void SeedRoster(Guid teamId, params string[] userIds)
    {
        foreach (var uid in userIds)
            _ctx.Add(new TeamMember { Id = Guid.NewGuid(), TeamId = teamId, UserId = uid, Role = "Member", IsDeleted = false, CreatedAt = DateTime.UtcNow });
    }

    private void SeedOccurrence(Guid scheduleId, string userId, bool isPrimary, int order,
        Instant? start = null, Instant? end = null)
    {
        _ctx.Add(new ScheduleOccurrence
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            RotationId = Guid.NewGuid(),
            UserId = userId,
            StartUtc = start ?? Now.Minus(Duration.FromHours(1)),
            EndUtc = end ?? Now.Plus(Duration.FromHours(1)),
            IsPrimary = isPrimary,
            Order = order,
            MaterializedAt = Now,
            CreatedAt = DateTime.UtcNow
        });
    }

    private void SeedOverride(Guid scheduleId, string userId, Instant start, Instant end, bool isActive = true)
    {
        _ctx.Add(new OnCallOverride
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            OverrideUserId = userId,
            StartUtc = start,
            EndUtc = end,
            IsActive = isActive,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        });
    }

    private Task SaveAsync() => _ctx.SaveChangesAsync();

    [Fact]
    public async Task PrimaryAndSecondary_NoOverride()
    {
        var team = Guid.NewGuid();
        var sched = SeedSchedule(team);
        SeedRoster(team, "alice", "bob");
        SeedOccurrence(sched, "alice", isPrimary: true, order: 1);
        SeedOccurrence(sched, "bob", isPrimary: false, order: 2);
        await SaveAsync();

        var status = await _sut.GetCurrentOnCallAsync(sched);

        Assert.NotNull(status);
        Assert.Equal("alice", status!.PrimaryUserId);
        Assert.Equal("bob", status.SecondaryUserId);
    }

    [Fact]
    public async Task Override_ReplacesBothSlots()
    {
        var team = Guid.NewGuid();
        var sched = SeedSchedule(team);
        SeedRoster(team, "alice", "bob", "charlie");
        SeedOccurrence(sched, "alice", isPrimary: true, order: 1);
        SeedOccurrence(sched, "bob", isPrimary: false, order: 2);
        SeedOverride(sched, "charlie", Now.Minus(Duration.FromHours(1)), Now.Plus(Duration.FromHours(2)));
        await SaveAsync();

        var status = await _sut.GetCurrentOnCallAsync(sched);

        Assert.NotNull(status);
        Assert.Equal("charlie", status!.PrimaryUserId);
        Assert.Contains("(Override)", status.PrimaryUserName);
        Assert.Null(status.SecondaryUserId);
    }

    [Fact]
    public async Task Override_OffRoster_IsSuppressed_FallsBackToRotation()
    {
        var team = Guid.NewGuid();
        var sched = SeedSchedule(team);
        SeedRoster(team, "alice", "bob");
        SeedOccurrence(sched, "alice", isPrimary: true, order: 1);
        SeedOccurrence(sched, "bob", isPrimary: false, order: 2);
        SeedOverride(sched, "charlie", Now.Minus(Duration.FromHours(1)), Now.Plus(Duration.FromHours(2)));
        await SaveAsync();

        var status = await _sut.GetCurrentOnCallAsync(sched);

        Assert.NotNull(status);
        Assert.Equal("alice", status!.PrimaryUserId);
        Assert.Equal("bob", status.SecondaryUserId);
    }

    [Fact]
    public async Task DepartedPrimary_SkippedToNextOrdered()
    {
        var team = Guid.NewGuid();
        var sched = SeedSchedule(team);
        SeedRoster(team, "bob");
        SeedOccurrence(sched, "alice", isPrimary: true, order: 1);
        SeedOccurrence(sched, "bob", isPrimary: false, order: 2);
        await SaveAsync();

        var status = await _sut.GetCurrentOnCallAsync(sched);

        Assert.NotNull(status);
        Assert.Equal("bob", status!.PrimaryUserId);
        Assert.Null(status.SecondaryUserId);
    }

    [Fact]
    public async Task NoOccurrenceAndNoOverride_ReturnsNull()
    {
        var team = Guid.NewGuid();
        var sched = SeedSchedule(team);
        SeedRoster(team, "alice");
        SeedOccurrence(sched, "alice", true, 1, Now.Minus(Duration.FromHours(5)), Now.Minus(Duration.FromHours(4)));
        await SaveAsync();

        Assert.Null(await _sut.GetCurrentOnCallAsync(sched));
    }

    [Fact]
    public async Task ScheduleNotFound_ReturnsNull()
    {
        Assert.Null(await _sut.GetCurrentOnCallAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task MostRecentlyStartedOverride_Wins()
    {
        var team = Guid.NewGuid();
        var sched = SeedSchedule(team);
        SeedRoster(team, "alice", "charlie", "dave");
        SeedOccurrence(sched, "alice", true, 1);
        SeedOverride(sched, "charlie", Now.Minus(Duration.FromHours(2)), Now.Plus(Duration.FromHours(1)));
        SeedOverride(sched, "dave", Now.Minus(Duration.FromMinutes(30)), Now.Plus(Duration.FromHours(1)));
        await SaveAsync();

        var status = await _sut.GetCurrentOnCallAsync(sched);

        Assert.Equal("dave", status!.PrimaryUserId);
    }

    [Fact]
    public async Task ForTeam_PicksFirstScheduleByName()
    {
        var team = Guid.NewGuid();
        var beta = SeedSchedule(team, "Beta");
        var alpha = SeedSchedule(team, "Alpha");
        SeedRoster(team, "alice", "bob");
        SeedOccurrence(alpha, "alice", true, 1);
        SeedOccurrence(beta, "bob", true, 1);
        await SaveAsync();

        var status = await _sut.GetCurrentOnCallForTeamAsync(team);

        Assert.Equal("alice", status!.PrimaryUserId);
        Assert.Equal(alpha, status.ScheduleId);
    }

    [Fact]
    public async Task ForTeam_NoSchedule_ReturnsNull()
    {
        Assert.Null(await _sut.GetCurrentOnCallForTeamAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task IsUserOnCall_PrimaryCoveringNow_True()
    {
        var sched = SeedSchedule(Guid.NewGuid());
        SeedOccurrence(sched, "alice", isPrimary: true, order: 1);
        await SaveAsync();

        Assert.True(await _sut.IsUserOnCallAsync("alice"));
    }

    [Fact]
    public async Task IsUserOnCall_SecondaryOnly_False()
    {
        var sched = SeedSchedule(Guid.NewGuid());
        SeedOccurrence(sched, "bob", isPrimary: false, order: 2);
        await SaveAsync();

        Assert.False(await _sut.IsUserOnCallAsync("bob"));
    }

    [Fact]
    public async Task IsUserOnCall_OutsideWindow_False()
    {
        var sched = SeedSchedule(Guid.NewGuid());
        SeedOccurrence(sched, "alice", true, 1, Now.Plus(Duration.FromHours(2)), Now.Plus(Duration.FromHours(4)));
        await SaveAsync();

        Assert.False(await _sut.IsUserOnCallAsync("alice"));
    }
}
