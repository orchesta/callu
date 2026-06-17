using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;

namespace Callu.Tests;

/// <summary>
/// Exercises the full occurrence-expansion loop (not just the static helpers): horizon/backlog
/// bounds, recurrence cadence, RecurrenceEndDate cutoff, one-off rotations, and wall-clock DST
/// spans in actual generated slots. GenerateOccurrences only touches the clock (for the
/// MaterializedAt stamp) and logger, so it can run with null infra dependencies.
/// </summary>
public class ScheduleMaterializerExpansionTests
{
    private static readonly DateTimeZone Utc = DateTimeZoneProviders.Tzdb["Etc/UTC"];
    private static readonly DateTimeZone NewYork = DateTimeZoneProviders.Tzdb["America/New_York"];

    private static ScheduleMaterializer Materializer() =>
        new(null!, null!, null!, null!, null!, SystemClock.Instance, NullLogger<ScheduleMaterializer>.Instance);

    private static ScheduleRotation Rotation(LocalDateTime handover, int shiftMinutes, RecurrenceType type,
        LocalDate? endDate = null, int? intervalDays = null) => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = Guid.NewGuid(),
        UserId = "on-call-user",
        HandoverStartLocal = handover,
        ShiftLengthMinutes = shiftMinutes,
        RecurrenceType = type,
        RecurrenceEndDate = endDate,
        RecurrenceIntervalDays = intervalDays,
        IsPrimary = true,
        Order = 3
    };

    [Fact]
    public void Daily_ProducesOneSlotPerDay_WithinHorizon()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 6, 1, 9, 0), 480, RecurrenceType.Daily);
        var now = Instant.FromUtc(2026, 6, 1, 8, 0);
        var horizon = now + Duration.FromDays(3);

        var occ = Materializer().GenerateOccurrences(schedule, rotation, Utc, now, horizon).ToList();

        Assert.Equal(3, occ.Count);
        Assert.All(occ, o => Assert.Equal(Duration.FromHours(8), o.EndUtc - o.StartUtc));
        Assert.Equal(Instant.FromUtc(2026, 6, 1, 9, 0), occ[0].StartUtc);
        Assert.Equal(Instant.FromUtc(2026, 6, 2, 9, 0), occ[1].StartUtc);
    }

    [Fact]
    public void Daily_PropagatesRotationMetadataOntoOccurrences()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 6, 1, 9, 0), 480, RecurrenceType.Daily);
        var now = Instant.FromUtc(2026, 6, 1, 8, 0);

        var first = Materializer().GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(1)).First();

        Assert.Equal(schedule.Id, first.ScheduleId);
        Assert.Equal(rotation.Id, first.RotationId);
        Assert.Equal("on-call-user", first.UserId);
        Assert.True(first.IsPrimary);
        Assert.Equal(3, first.Order);
    }

    [Fact]
    public void None_ProducesExactlyOneSlot()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 6, 1, 9, 0), 480, RecurrenceType.None);
        var now = Instant.FromUtc(2026, 6, 1, 8, 0);

        var occ = Materializer().GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(30)).ToList();

        Assert.Single(occ);
    }

    [Fact]
    public void RecurrenceEndDate_StopsGeneration()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 6, 1, 9, 0), 480, RecurrenceType.Daily,
            endDate: new LocalDate(2026, 6, 2));
        var now = Instant.FromUtc(2026, 6, 1, 8, 0);

        var occ = Materializer().GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(30)).ToList();

        Assert.Equal(2, occ.Count);
    }

    [Fact]
    public void NonPositiveShiftLength_ProducesNothing()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 6, 1, 9, 0), 0, RecurrenceType.Daily);
        var now = Instant.FromUtc(2026, 6, 1, 8, 0);

        Assert.Empty(Materializer().GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(5)));
    }

    [Fact]
    public void DailyShift_OverSpringForwardDay_Spans23WallClockHours()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 3, 8, 0, 0), 1440, RecurrenceType.Daily);
        var now = Instant.FromUtc(2026, 3, 8, 4, 0);
        var horizon = now + Duration.FromDays(2);

        var first = Materializer().GenerateOccurrences(schedule, rotation, NewYork, now, horizon).First();

        Assert.Equal(Duration.FromHours(23), first.EndUtc - first.StartUtc);
    }

    [Fact]
    public void IntervalDays_OverridesEnumCadence_InExpansion()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2026, 6, 1, 9, 0), 480, RecurrenceType.Weekly, intervalDays: 2);
        var now = Instant.FromUtc(2026, 6, 1, 8, 0);

        var occ = Materializer().GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(5)).ToList();

        Assert.Equal(Instant.FromUtc(2026, 6, 1, 9, 0), occ[0].StartUtc);
        Assert.Equal(Instant.FromUtc(2026, 6, 3, 9, 0), occ[1].StartUtc);
    }

    [Fact]
    public void FarPastAnchor_FastForwards_IntoTheWindow()
    {
        var schedule = new Schedule { Id = Guid.NewGuid() };
        var rotation = Rotation(new LocalDateTime(2025, 6, 1, 9, 0), 480, RecurrenceType.Daily);
        var now = Instant.FromUtc(2026, 6, 1, 8, 0);

        var occ = Materializer().GenerateOccurrences(schedule, rotation, Utc, now, now + Duration.FromDays(2)).ToList();

        Assert.NotEmpty(occ);
        Assert.All(occ, o => Assert.True(o.EndUtc > now - Duration.FromHours(1)));
        Assert.All(occ, o => Assert.True(o.StartUtc <= now + Duration.FromDays(2)));
    }
}
