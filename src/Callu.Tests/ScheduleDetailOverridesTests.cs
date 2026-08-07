using Callu.Application.Services;
using Callu.Application.Validators;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Schedules;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The schedule detail has to report the overrides that decide who a page reaches.</summary>
public class ScheduleDetailOverridesTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly ServiceProvider _sp;
    private readonly IOnCallOverrideService _overrides = Substitute.For<IOnCallOverrideService>();
    private readonly ScheduleService _sut;
    private readonly Guid _scheduleId = Guid.NewGuid();

    public ScheduleDetailOverridesTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"sch-ovr-{Guid.NewGuid():N}").Options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        _sp = services.BuildServiceProvider();

        var tz = DateTimeZoneProviders.Tzdb;
        var users = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        _sut = new ScheduleService(
            new ScheduleRepository(_ctx, NullLogger<ScheduleRepository>.Instance),
            new ScheduleOccurrenceRepository(_ctx, NullLogger<ScheduleOccurrenceRepository>.Instance),
            new ScheduleRotationRepository(_ctx, NullLogger<ScheduleRotationRepository>.Instance),
            new EscalationStepRepository(_ctx, NullLogger<EscalationStepRepository>.Instance),
            new TeamMemberRepository(_ctx, NullLogger<TeamMemberRepository>.Instance),
            new SavingTransactionManager(_ctx),
            users,
            new CreateScheduleRequestValidator(tz),
            new SaveSchedulePlanRequestValidator(tz),
            Substitute.For<IScheduleMaterializer>(),
            tz,
            _overrides,
            Substitute.For<IAuditLogService>(),
            _sp.GetRequiredService<HybridCache>(),
            NullLogger<ScheduleService>.Instance,
            Substitute.For<IOnCallService>());

        var teamId = Guid.NewGuid();
        // Team is a required navigation, and the detail query includes it.
        _ctx.Add(new Team { Id = teamId, Name = "Test", CreatedAt = DateTime.UtcNow });
        _ctx.Add(new Schedule
        {
            Id = _scheduleId,
            Name = "Kurumsal Uygulamalar Takvimi",
            TeamId = teamId,
            Timezone = "Europe/Istanbul",
            CreatedAt = DateTime.UtcNow,
        });
        _ctx.SaveChanges();
    }

    public void Dispose() { _ctx.Dispose(); _sp.Dispose(); }

    private OnCallOverrideDto Cover(string who) => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = _scheduleId,
        ScheduleName = "Kurumsal Uygulamalar Takvimi",
        OverrideUserId = Guid.NewGuid().ToString(),
        OverrideUserName = who,
        StartUtc = DateTime.UtcNow.AddMinutes(-1),
        EndUtc = DateTime.UtcNow.AddHours(2),
        IsActive = true,
    };

    [Fact]
    public async Task ASchedulesOverrides_ComeBackWithItsDetail()
    {
        _overrides.GetOverridesAsync(_scheduleId, Arg.Any<CancellationToken>())
            .Returns([Cover("Weekend Cover")]);

        var detail = await _sut.GetScheduleByIdAsync(_scheduleId);

        // This was a hardcoded empty list, so an active override reported as none.
        Assert.NotNull(detail);
        var only = Assert.Single(detail!.Overrides);
        Assert.Equal("Weekend Cover", only.OverrideUserName);
        Assert.True(only.IsActive);
    }

    [Fact]
    public async Task ASchedule_WithoutOverrides_ReportsAnEmptyList()
    {
        _overrides.GetOverridesAsync(_scheduleId, Arg.Any<CancellationToken>())
            .Returns([]);

        var detail = await _sut.GetScheduleByIdAsync(_scheduleId);

        Assert.NotNull(detail);
        Assert.Empty(detail!.Overrides);
    }

    [Fact]
    public async Task TheDetail_AsksTheOverrideServiceForTheScheduleItWasAskedAbout()
    {
        _overrides.GetOverridesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);

        await _sut.GetScheduleByIdAsync(_scheduleId);

        await _overrides.Received(1).GetOverridesAsync(_scheduleId, Arg.Any<CancellationToken>());
    }
}
