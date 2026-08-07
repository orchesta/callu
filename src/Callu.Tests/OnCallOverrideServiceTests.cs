using Callu.Application.Validators;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Schedules;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;
using Callu.Application.Services;
using Callu.Domain.Enums;

namespace Callu.Tests;

/// <summary>An override naming an off-team user is rejected on write rather than ignored on read.</summary>
public class OnCallOverrideServiceTests : IDisposable
{
    private static readonly Instant Now = Instant.FromUtc(2026, 6, 15, 12, 0);
    private static readonly DateTime Start = DateTime.SpecifyKind(new DateTime(2026, 6, 16, 9, 0, 0), DateTimeKind.Utc);
    private static readonly DateTime End = DateTime.SpecifyKind(new DateTime(2026, 6, 16, 17, 0, 0), DateTimeKind.Utc);

    private readonly ApplicationDbContext _ctx;
    private readonly ServiceProvider _sp;
    private readonly OnCallOverrideService _sut;
    private readonly IAuditLogService _audit = Substitute.For<IAuditLogService>();
    private readonly Guid _teamId = Guid.NewGuid();
    private readonly Guid _scheduleId = Guid.NewGuid();

    public OnCallOverrideServiceTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"override-{Guid.NewGuid():N}").Options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        _sp = services.BuildServiceProvider();

        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        _sut = new OnCallOverrideService(
            new OnCallOverrideRepository(_ctx, NullLogger<OnCallOverrideRepository>.Instance),
            new ScheduleRepository(_ctx, NullLogger<ScheduleRepository>.Instance),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            new SavingTransactionManager(_ctx),
            MockUserManager("alice", "bob", "carol"),
            new CreateOverrideRequestValidator(),
            new UpdateOverrideRequestValidator(),
            _sp.GetRequiredService<HybridCache>(),
            clock,
            _audit,
            NullLogger<OnCallOverrideService>.Instance);

        _ctx.Add(new Schedule
        {
            Id = _scheduleId,
            Name = "Primary",
            TeamId = _teamId,
            Timezone = "UTC",
            CreatedAt = DateTime.UtcNow
        });
        SeedRoster("alice", "bob");
        _ctx.SaveChanges();
    }

    public void Dispose() { _ctx.Dispose(); _sp.Dispose(); }

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
                Email = $"{id}@example.io",
                UserName = $"{id}@example.io"
            });
        return mgr;
    }

    private void SeedRoster(params string[] userIds)
    {
        foreach (var id in userIds)
            _ctx.Add(new TeamMember
            {
                Id = Guid.NewGuid(),
                TeamId = _teamId,
                UserId = id,
                Role = "Member",
                CreatedAt = DateTime.UtcNow
            });
    }

    private CreateOverrideRequest Request(string overrideUserId, string? originalUserId = null) => new()
    {
        ScheduleId = _scheduleId,
        OverrideUserId = overrideUserId,
        OriginalUserId = originalUserId,
        StartUtc = Start,
        EndUtc = End,
        Reason = "Swap"
    };

    [Fact]
    public async Task Create_TeamMemberOverride_IsAccepted()
    {
        var dto = await _sut.CreateOverrideAsync(Request("bob", originalUserId: "alice"));

        Assert.Equal("bob", dto.OverrideUserId);
        Assert.Single(await _ctx.OnCallOverrides.ToListAsync());
    }

    /// <summary>The audit actions this run produced.</summary>
    private IReadOnlyList<AuditAction> AuditedActions() =>
        _audit.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IAuditLogService.LogAsync))
            .Select(c => (AuditAction)c.GetArguments()[1]!)
            .ToList();

    [Fact]
    public async Task Create_RecordsWhoTookOverTheRota()
    {
        var dto = await _sut.CreateOverrideAsync(Request("bob", originalUserId: "alice"));

        Assert.Contains(AuditAction.OverrideCreated, AuditedActions());
        await _audit.Received(1).LogAsync(
            "bob", AuditAction.OverrideCreated, "OnCallOverride", dto.Id.ToString(),
            Arg.Any<string?>(), Arg.Is<string?>(v => v != null && v.Contains("bob")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_RecordsThatTheRotaWentBack()
    {
        var dto = await _sut.CreateOverrideAsync(Request("bob", originalUserId: "alice"));

        await _sut.DeleteOverrideAsync(dto.Id);

        Assert.Contains(AuditAction.OverrideCancelled, AuditedActions());
    }

    /// <summary>A rejected override changed nothing, so it must not claim it did.</summary>
    [Fact]
    public async Task ARejectedOverride_RecordsNothing()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CreateOverrideAsync(Request("carol")));

        Assert.DoesNotContain(AuditAction.OverrideCreated, AuditedActions());
    }

    [Fact]
    public async Task Create_OffTeamOverrideUser_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CreateOverrideAsync(Request("carol")));

        Assert.Contains("not a member", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _ctx.OnCallOverrides.ToListAsync());
    }

    [Fact]
    public async Task Create_OffTeamOriginalUser_IsRejected()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.CreateOverrideAsync(Request("bob", originalUserId: "carol")));

        Assert.Empty(await _ctx.OnCallOverrides.ToListAsync());
    }

    [Fact]
    public async Task Create_RemovedTeamMember_IsRejected()
    {
        var membership = await _ctx.TeamMembers.SingleAsync(m => m.UserId == "bob");
        membership.IsDeleted = true;
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.CreateOverrideAsync(Request("bob")));
    }

    [Fact]
    public async Task Create_UnknownSchedule_IsRejected()
    {
        var request = Request("bob") with { ScheduleId = Guid.NewGuid() };

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateOverrideAsync(request));
    }

    [Fact]
    public async Task Update_SwitchingToAnOffTeamUser_IsRejected()
    {
        var created = await _sut.CreateOverrideAsync(Request("bob"));
        _ctx.ChangeTracker.Clear();

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.UpdateOverrideAsync(created.Id, new UpdateOverrideRequest { OverrideUserId = "carol" }));

        _ctx.ChangeTracker.Clear();
        var stored = await _ctx.OnCallOverrides.SingleAsync();
        Assert.Equal("bob", stored.OverrideUserId);
    }

    [Fact]
    public async Task Update_SwitchingToAnotherTeamMember_IsAccepted()
    {
        var created = await _sut.CreateOverrideAsync(Request("bob"));
        _ctx.ChangeTracker.Clear();

        var scheduleId = await _sut.UpdateOverrideAsync(
            created.Id, new UpdateOverrideRequest { OverrideUserId = "alice" });

        Assert.Equal(_scheduleId, scheduleId);
        _ctx.ChangeTracker.Clear();
        Assert.Equal("alice", (await _ctx.OnCallOverrides.SingleAsync()).OverrideUserId);
    }

    [Fact]
    public async Task Update_LeavingTheUserUnchanged_SkipsTheMembershipCheck()
    {
        var created = await _sut.CreateOverrideAsync(Request("bob"));
        _ctx.ChangeTracker.Clear();

        var scheduleId = await _sut.UpdateOverrideAsync(
            created.Id, new UpdateOverrideRequest { Reason = "Extended cover" });

        Assert.Equal(_scheduleId, scheduleId);
    }
}
