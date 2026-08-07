using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using NodaTime;

namespace Callu.Tests;

/// <summary>Locks in the wall-clock DST behaviour the materializer depends on.</summary>
public class ScheduleMaterializerTests
{
    private static readonly DateTimeZone NewYork = DateTimeZoneProviders.Tzdb["America/New_York"];

    [Fact]
    public void ResolveHandover_NormalDay_UsesSummerOffset()
    {
        var instant = ScheduleMaterializer.ResolveHandoverInZone(new LocalDateTime(2026, 6, 1, 9, 0), NewYork);
        Assert.Equal(Instant.FromUtc(2026, 6, 1, 13, 0), instant);
    }

    [Fact]
    public void ResolveHandover_SpringForwardGap_ShiftsForward()
    {
        var instant = ScheduleMaterializer.ResolveHandoverInZone(new LocalDateTime(2026, 3, 8, 2, 30), NewYork);
        Assert.Equal(Instant.FromUtc(2026, 3, 8, 7, 30), instant);
    }

    [Fact]
    public void ResolveHandover_FallBackAmbiguity_PicksEarlier()
    {
        var instant = ScheduleMaterializer.ResolveHandoverInZone(new LocalDateTime(2026, 11, 1, 1, 30), NewYork);
        Assert.Equal(Instant.FromUtc(2026, 11, 1, 5, 30), instant);
    }

    [Fact]
    public void WallClockShift_SpringForwardDay_SpansOneHourLess()
    {
        var start = new LocalDateTime(2026, 3, 8, 1, 0);
        var startUtc = ScheduleMaterializer.ResolveHandoverInZone(start, NewYork);
        var endUtc = ScheduleMaterializer.ResolveHandoverInZone(start.PlusMinutes(180), NewYork);

        Assert.Equal(Duration.FromHours(2), endUtc - startUtc);
    }

    [Fact]
    public void WallClockShift_FallBackDay_SpansOneHourMore()
    {
        var start = new LocalDateTime(2026, 11, 1, 0, 0);
        var startUtc = ScheduleMaterializer.ResolveHandoverInZone(start, NewYork);
        var endUtc = ScheduleMaterializer.ResolveHandoverInZone(start.PlusMinutes(180), NewYork);

        Assert.Equal(Duration.FromHours(4), endUtc - startUtc);
    }

    [Theory]
    [InlineData(RecurrenceType.Daily, 1)]
    [InlineData(RecurrenceType.Weekly, 7)]
    [InlineData(RecurrenceType.Biweekly, 14)]
    public void PeriodStart_FollowsEnumCadence_InDays(RecurrenceType type, int expectedDays)
    {
        var anchor = new LocalDateTime(2026, 6, 1, 9, 0);
        var rotation = new ScheduleRotation { RecurrenceType = type };

        Assert.Equal(anchor.PlusDays(expectedDays), ScheduleMaterializer.PeriodStart(anchor, rotation, 1));
        Assert.Equal(anchor.PlusDays(expectedDays * 3), ScheduleMaterializer.PeriodStart(anchor, rotation, 3));
    }

    [Fact]
    public void PeriodStart_Monthly_IsCalendarAligned_ForOneStep()
    {
        var anchor = new LocalDateTime(2026, 1, 31, 9, 0);

        var next = ScheduleMaterializer.PeriodStart(anchor, new ScheduleRotation { RecurrenceType = RecurrenceType.Monthly }, 1);

        Assert.Equal(anchor.PlusMonths(1), next);
    }

    [Fact]
    public void PeriodStart_MonthEndAnchor_DoesNotDriftAcrossPeriods()
    {
        var anchor = new LocalDateTime(2026, 1, 31, 9, 0);
        var rotation = new ScheduleRotation { RecurrenceType = RecurrenceType.Monthly };

        for (var period = 0; period <= 12; period++)
        {
            Assert.Equal(anchor.PlusMonths(period), ScheduleMaterializer.PeriodStart(anchor, rotation, period));
        }

        Assert.Equal(new LocalDateTime(2026, 3, 31, 9, 0), ScheduleMaterializer.PeriodStart(anchor, rotation, 2));
    }

    [Fact]
    public void PeriodStart_IntervalDays_OverridesEnum()
    {
        var anchor = new LocalDateTime(2026, 6, 1, 9, 0);
        var rotation = new ScheduleRotation { RecurrenceType = RecurrenceType.Weekly, RecurrenceIntervalDays = 2 };

        Assert.Equal(anchor.PlusDays(2), ScheduleMaterializer.PeriodStart(anchor, rotation, 1));
        Assert.Equal(anchor.PlusDays(6), ScheduleMaterializer.PeriodStart(anchor, rotation, 3));
    }

    [Theory]
    [InlineData(RecurrenceType.Daily, 1)]
    [InlineData(RecurrenceType.Weekly, 7)]
    [InlineData(RecurrenceType.Biweekly, 14)]
    public void GetPeriodDays_MatchesEnumCadence(RecurrenceType type, int expected)
    {
        Assert.Equal(expected, ScheduleMaterializer.GetPeriodDays(new ScheduleRotation { RecurrenceType = type }));
    }

    [Fact]
    public void GetPeriodDays_PrefersIntervalDays()
    {
        Assert.Equal(21, ScheduleMaterializer.GetPeriodDays(
            new ScheduleRotation { RecurrenceType = RecurrenceType.Weekly, RecurrenceIntervalDays = 21 }));
    }
}
