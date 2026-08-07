using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;

namespace Callu.Tests;

/// <summary>A partial-day shift emits one daily window per owned day, while legacy rows keep the single-block behaviour.</summary>
public class ScheduleMaterializerOwnershipTests
{
    private static readonly DateTimeZone Utc = DateTimeZoneProviders.Tzdb["Etc/UTC"];
    private static readonly DateTimeZone NewYork = DateTimeZoneProviders.Tzdb["America/New_York"];

    private static ScheduleMaterializer Materializer() =>
        new(null!, null!, null!, null!, null!, SystemClock.Instance, NullLogger<ScheduleMaterializer>.Instance);

    private static ScheduleRotation Rotation(
        LocalDateTime handover,
        int shiftMinutes,
        int? ownershipDays,
        int intervalDays,
        int order = 0,
        string userId = "alice",
        LocalDate? endDate = null) => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = Guid.NewGuid(),
        UserId = userId,
        HandoverStartLocal = handover,
        ShiftLengthMinutes = shiftMinutes,
        OwnershipDays = ownershipDays,
        RecurrenceType = RecurrenceType.Weekly,
        RecurrenceIntervalDays = intervalDays,
        RecurrenceEndDate = endDate,
        IsPrimary = true,
        Order = order
    };

    [Fact]
    public void OwnershipDays_EmitsOneDailyWindowPerOwnedDay()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        // One member owns a full week of 09:00–17:00 days, cycling every 7 days.
        var rotation = Rotation(new LocalDateTime(2026, 7, 6, 9, 0), 480, ownershipDays: 7, intervalDays: 7);
        var now = Instant.FromUtc(2026, 7, 6, 8, 0);

        var occ = Materializer()
            .GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(7))
            .ToList();

        Assert.Equal(7, occ.Count);
        Assert.All(occ, o => Assert.Equal(Duration.FromHours(8), o.EndUtc - o.StartUtc));
        Assert.Equal(
            Enumerable.Range(0, 7).Select(d => Instant.FromUtc(2026, 7, 6 + d, 9, 0)),
            occ.Select(o => o.StartUtc));
    }

    [Fact]
    public void TwoMemberWeeklyRotation_CoversEveryDayOfTheHorizon()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var anchor = new LocalDateTime(2026, 7, 6, 9, 0);
        // Two members, one week each → 14-day cycle, each owning their 7 days.
        var alice = Rotation(anchor, 480, ownershipDays: 7, intervalDays: 14, order: 0, userId: "alice");
        var bob = Rotation(anchor.PlusDays(7), 480, ownershipDays: 7, intervalDays: 14, order: 1, userId: "bob");

        var now = Instant.FromUtc(2026, 7, 6, 8, 0);
        var horizon = now + Duration.FromDays(28);
        var sut = Materializer();

        var occ = sut.GenerateOccurrences(schedule, alice, Utc, now, horizon)
            .Concat(sut.GenerateOccurrences(schedule, bob, Utc, now, horizon))
            .ToList();

        // Every calendar day in the horizon has exactly one 09:00 window, and it belongs to
        // whichever member owns that week.
        for (var day = 0; day < 28; day++)
        {
            var start = Instant.FromUtc(2026, 7, 6, 9, 0) + Duration.FromDays(day);
            var covering = occ.Where(o => o.StartUtc == start).ToList();

            var owner = Assert.Single(covering);
            var expected = day / 7 % 2 == 0 ? "alice" : "bob";
            Assert.Equal(expected, owner.UserId);
        }
    }

    [Fact]
    public void OwnershipDays_LeavesNoGapBetweenCycles()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 7, 6, 9, 0), 480, ownershipDays: 7, intervalDays: 7);
        var now = Instant.FromUtc(2026, 7, 6, 8, 0);

        var starts = Materializer()
            .GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(21))
            .Select(o => o.StartUtc)
            .ToList();

        Assert.Equal(21, starts.Count);
        Assert.Equal(starts.Distinct().Count(), starts.Count);
        // Consecutive windows are exactly one day apart across the cycle boundary too.
        Assert.All(
            starts.Zip(starts.Skip(1), (a, b) => b - a),
            gap => Assert.Equal(Duration.FromDays(1), gap));
    }

    [Fact]
    public void NullOwnershipDays_KeepsTheLegacySingleBlockPerCycle()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        // 24/7: the shift length already spans the whole owned block, so one occurrence per cycle.
        var rotation = Rotation(new LocalDateTime(2026, 7, 6, 0, 0), 7 * 1440, ownershipDays: null, intervalDays: 14);
        var now = Instant.FromUtc(2026, 7, 6, 0, 0);

        var occ = Materializer()
            .GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(20))
            .ToList();

        Assert.Equal(2, occ.Count);
        Assert.All(occ, o => Assert.Equal(Duration.FromDays(7), o.EndUtc - o.StartUtc));
        Assert.Equal(Instant.FromUtc(2026, 7, 6, 0, 0), occ[0].StartUtc);
        Assert.Equal(Instant.FromUtc(2026, 7, 20, 0, 0), occ[1].StartUtc);
    }

    [Fact]
    public void OwnershipDays_RespectsRecurrenceEndDate()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 7, 6, 9, 0), 480, ownershipDays: 7, intervalDays: 7,
            endDate: new LocalDate(2026, 7, 8));
        var now = Instant.FromUtc(2026, 7, 6, 8, 0);

        var occ = Materializer()
            .GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(30))
            .ToList();

        Assert.Equal(3, occ.Count);
        Assert.Equal(Instant.FromUtc(2026, 7, 8, 9, 0), occ[^1].StartUtc);
    }

    [Fact]
    public void OwnershipDays_AcrossSpringForward_KeepsOneWindowPerDay()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        // 2026-03-08 is the US spring-forward day; a 09:00–17:00 window is unaffected by the
        // 02:00 transition, but the day must still get exactly one occurrence.
        var rotation = Rotation(new LocalDateTime(2026, 3, 6, 9, 0), 480, ownershipDays: 7, intervalDays: 7);
        var now = Instant.FromUtc(2026, 3, 6, 13, 0);

        var occ = Materializer()
            .GenerateOccurrences(schedule, rotation, NewYork, now, now + Duration.FromDays(6))
            .ToList();

        Assert.Equal(7, occ.Count);
        Assert.All(occ, o => Assert.Equal(Duration.FromHours(8), o.EndUtc - o.StartUtc));

        // Pre-transition days start at 14:00 UTC (EST), post-transition at 13:00 UTC (EDT).
        Assert.Equal(Instant.FromUtc(2026, 3, 7, 14, 0), occ[1].StartUtc);
        Assert.Equal(Instant.FromUtc(2026, 3, 8, 13, 0), occ[2].StartUtc);
    }

    [Fact]
    public void OwnershipDays_ZeroOrNegative_FallsBackToASingleBlock()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 7, 6, 9, 0), 480, ownershipDays: 0, intervalDays: 7);
        var now = Instant.FromUtc(2026, 7, 6, 8, 0);

        var occ = Materializer()
            .GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(14))
            .ToList();

        Assert.Equal(2, occ.Count);
    }
}
