using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

public class UptimeCalculatorTests : IDisposable
{
    private static readonly DateTime From = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _ctx;
    private readonly UptimeCalculator _sut;
    private readonly Guid _serviceId = Guid.NewGuid();

    public UptimeCalculatorTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"uptime-{Guid.NewGuid():N}").Options);

        _ctx.Services.Add(new Service { Id = _serviceId, Name = "api" });
        _ctx.SaveChanges();

        _sut = new UptimeCalculator(
            new ServiceRepository(_ctx, NullLogger<ServiceRepository>.Instance),
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance));
    }

    public void Dispose() => _ctx.Dispose();

    private void AddIncident(DateTime start, DateTime end)
    {
        _ctx.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(),
            Title = "down",
            ServiceId = _serviceId,
            StartedAt = start,
            CreatedAt = start,
            ResolvedAt = end,
            Status = IncidentStatus.Resolved
        });
        _ctx.SaveChanges();
    }

    [Fact]
    public async Task Overlapping_incidents_count_once()
    {
        AddIncident(From.AddHours(1), From.AddHours(13));
        AddIncident(From.AddHours(2), From.AddHours(14));

        var result = (await _sut.ComputeAsync(From, To)).Single();

        Assert.Equal(13 * 60, result.TotalDowntimeMinutes);
        Assert.Equal(45.83, result.UptimePercent);
    }

    [Fact]
    public async Task Disjoint_incidents_add_up()
    {
        AddIncident(From.AddHours(1), From.AddHours(3));
        AddIncident(From.AddHours(5), From.AddHours(6));

        var result = (await _sut.ComputeAsync(From, To)).Single();

        Assert.Equal(3 * 60, result.TotalDowntimeMinutes);
    }

    [Fact]
    public async Task Uptime_never_goes_negative()
    {
        for (var i = 0; i < 5; i++)
            AddIncident(From, To);

        var result = (await _sut.ComputeAsync(From, To)).Single();

        Assert.Equal(0, result.UptimePercent);
        Assert.Equal(24 * 60, result.TotalDowntimeMinutes);
    }
}
