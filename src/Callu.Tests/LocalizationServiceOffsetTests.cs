using Callu.Infrastructure.Services;
using NodaTime;

namespace Callu.Tests;

/// <summary>
/// The timezone list is served from a singleton, so its UTC offsets have to be read at request time
/// rather than snapshotted at boot.
/// </summary>
public class LocalizationServiceOffsetTests
{
    private const string DstZone = "Europe/Berlin";

    private static readonly Instant Winter = Instant.FromUtc(2026, 1, 15, 12, 0);
    private static readonly Instant Summer = Instant.FromUtc(2026, 7, 15, 12, 0);

    private sealed class MovableClock(Instant now) : IClock
    {
        public Instant Now { get; set; } = now;

        public Instant GetCurrentInstant() => Now;
    }

    private static LocalizationService Service(IClock clock) =>
        new(DateTimeZoneProviders.Tzdb, clock);

    [Fact]
    public void TheOffsetFollowsTheClock_AcrossADstTransition()
    {
        var clock = new MovableClock(Winter);
        var service = Service(clock);

        Assert.Equal(TimeSpan.FromHours(1), service.GetTimezone(DstZone)!.BaseUtcOffset);

        clock.Now = Summer;

        Assert.Equal(TimeSpan.FromHours(2), service.GetTimezone(DstZone)!.BaseUtcOffset);
    }

    [Fact]
    public void TheDisplayedLabelFollowsTheClockToo()
    {
        var clock = new MovableClock(Winter);
        var service = Service(clock);

        Assert.Equal("UTC+01:00", service.GetTimezone(DstZone)!.OffsetString);

        clock.Now = Summer;

        var summer = service.GetTimezones().Single(t => t.Id == DstZone);
        Assert.Equal("UTC+02:00", summer.OffsetString);
        Assert.Equal($"(UTC+02:00) {DstZone}", summer.DisplayName);
    }

    [Fact]
    public void TheZonePremise_IsAZoneThatActuallyMoves()
    {
        var zone = DateTimeZoneProviders.Tzdb[DstZone];

        Assert.NotEqual(zone.GetUtcOffset(Winter), zone.GetUtcOffset(Summer));
    }

    [Fact]
    public void TheListIsStillComplete_AndOrderedByTheCurrentOffset()
    {
        var service = Service(new MovableClock(Summer));

        var zones = service.GetTimezones().ToList();

        Assert.Equal(DateTimeZoneProviders.Tzdb.Ids.Count, zones.Count);
        Assert.Equal(zones.OrderBy(z => z.BaseUtcOffset).ThenBy(z => z.Id).ToList(), zones);
        Assert.Contains(zones, z => z.Id == "UTC");
    }
}
