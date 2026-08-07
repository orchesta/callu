using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
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

namespace Callu.Tests;

public class RotationCoverageTests : IDisposable
{
    private static readonly Instant Now = Instant.FromUtc(2026, 8, 1, 0, 0);

    private readonly ApplicationDbContext _ctx;
    private readonly ServiceProvider _sp;
    private readonly RotationService _sut;
    private readonly Guid _scheduleId = Guid.NewGuid();

    public RotationCoverageTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"coverage-{Guid.NewGuid():N}").Options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        _sp = services.BuildServiceProvider();

        _sut = new RotationService(
            Substitute.For<IScheduleRotationRepository>(),
            Substitute.For<IScheduleRepository>(),
            new ScheduleOccurrenceRepository(_ctx, NullLogger<ScheduleOccurrenceRepository>.Instance),
            Substitute.For<ITeamMemberRepository>(),
            new SavingTransactionManager(_ctx),
            MockUserManager(),
            Substitute.For<IValidator<CreateRotationRequest>>(),
            Substitute.For<IValidator<UpdateRotationRequest>>(),
            Substitute.For<IScheduleMaterializer>(),
            _sp.GetRequiredService<HybridCache>(),
            new FixedClock(Now),
            NullLogger<RotationService>.Instance);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _sp.Dispose();
    }

    [Fact]
    public async Task NoPrimaryOccurrences_IsZeroCoverage_WithFullHorizonGap()
    {
        var result = await _sut.ValidateRotationCoverageAsync(_scheduleId, days: 2);

        Assert.False(result.HasFullCoverage);
        Assert.Equal(0, result.CoveragePercent);
        Assert.Equal(48, result.GapHours);
        Assert.Single(result.Gaps);
        Assert.Equal(Now.ToDateTimeUtc(), result.Gaps[0].Start);
        Assert.Equal(Now.Plus(Duration.FromDays(2)).ToDateTimeUtc(), result.Gaps[0].End);
    }

    [Fact]
    public async Task ContiguousPrimaryCoverage_ReportsFull()
    {
        SeedPrimary(Now.Minus(Duration.FromHours(1)), Now.Plus(Duration.FromDays(10)));
        await _ctx.SaveChangesAsync();

        var result = await _sut.ValidateRotationCoverageAsync(_scheduleId, days: 7);

        Assert.True(result.HasFullCoverage);
        Assert.Equal(0, result.GapHours);
        Assert.Equal(100, result.CoveragePercent);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public async Task HoleBetweenPrimarySlots_IsReportedAsGap()
    {
        SeedPrimary(Now, Now.Plus(Duration.FromDays(1)));
        SeedPrimary(Now.Plus(Duration.FromDays(2)), Now.Plus(Duration.FromDays(7)));
        await _ctx.SaveChangesAsync();

        var result = await _sut.ValidateRotationCoverageAsync(_scheduleId, days: 7);

        Assert.False(result.HasFullCoverage);
        Assert.Equal(24, result.GapHours);
        Assert.Single(result.Gaps);
        Assert.Equal(Now.Plus(Duration.FromDays(1)).ToDateTimeUtc(), result.Gaps[0].Start);
        Assert.Equal(Now.Plus(Duration.FromDays(2)).ToDateTimeUtc(), result.Gaps[0].End);
        Assert.Equal(85.7, result.CoveragePercent);
    }

    [Fact]
    public async Task SecondaryOnly_DoesNotCountAsCoverage()
    {
        _ctx.Add(Occurrence(
            Now.Minus(Duration.FromHours(1)),
            Now.Plus(Duration.FromDays(10)),
            isPrimary: false));
        await _ctx.SaveChangesAsync();

        var result = await _sut.ValidateRotationCoverageAsync(_scheduleId, days: 3);

        Assert.False(result.HasFullCoverage);
        Assert.Equal(0, result.CoveragePercent);
        Assert.Equal(72, result.GapHours);
    }

    private void SeedPrimary(Instant start, Instant end) =>
        _ctx.Add(Occurrence(start, end, isPrimary: true));

    private ScheduleOccurrence Occurrence(Instant start, Instant end, bool isPrimary) => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = _scheduleId,
        RotationId = Guid.NewGuid(),
        UserId = "alice",
        StartUtc = start,
        EndUtc = end,
        IsPrimary = isPrimary,
        Order = 1,
        MaterializedAt = Now,
        CreatedAt = DateTime.UtcNow
    };

    private static UserManager<ApplicationUser> MockUserManager()
    {
        return Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
    }
}
