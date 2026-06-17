using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using NodaTime;

namespace Callu.Tests;

/// <summary>
/// Locks in the timezone/DST behaviour the materializer depends on. Shifts are WALL-CLOCK,
/// so on DST-transition days a fixed nominal length legitimately spans 23 or 25 real hours.
/// Uses America/New_York: spring-forward 2026-03-08 02:00→03:00, fall-back 2026-11-01 02:00→01:00.
/// </summary>
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
    public void Advance_FollowsEnumCadence_InDays(RecurrenceType type, int expectedDays)
    {
        var start = new LocalDateTime(2026, 6, 1, 9, 0);
        var next = ScheduleMaterializer.Advance(start, new ScheduleRotation { RecurrenceType = type });

        Assert.Equal(start.PlusDays(expectedDays), next);
    }

    [Fact]
    public void Advance_Monthly_IsCalendarAligned()
    {
        var start = new LocalDateTime(2026, 1, 31, 9, 0);
        var next = ScheduleMaterializer.Advance(start, new ScheduleRotation { RecurrenceType = RecurrenceType.Monthly });

        Assert.Equal(start.PlusMonths(1), next);
    }

    [Fact]
    public void Advance_IntervalDays_OverridesEnum()
    {
        var start = new LocalDateTime(2026, 6, 1, 9, 0);
        var next = ScheduleMaterializer.Advance(start,
            new ScheduleRotation { RecurrenceType = RecurrenceType.Weekly, RecurrenceIntervalDays = 2 });

        Assert.Equal(start.PlusDays(2), next);
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
