using System.Globalization;
using NodaTime;
using NodaTime.TimeZones;
using Callu.Application.Services;
using Callu.Shared.Models.Settings;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Localization service implementation — uses NodaTime tzdb for IANA-based timezone data
/// so the same IDs work on Linux containers, Windows hosts, and the frontend picker.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private static readonly ZoneLocalMappingResolver DstResolver =
        Resolvers.CreateMappingResolver(Resolvers.ReturnEarlier, Resolvers.ReturnForwardShifted);

    private readonly IDateTimeZoneProvider _tzProvider;
    private readonly IClock _clock;
    private readonly List<TimezoneDto> _timezones;
    private readonly List<CultureDto> _cultures;

    public LocalizationService(IDateTimeZoneProvider tzProvider, IClock clock)
    {
        _tzProvider = tzProvider;
        _clock = clock;
        _timezones = LoadTimezones(tzProvider, clock);
        _cultures = LoadCultures();
    }

    private static List<TimezoneDto> LoadTimezones(IDateTimeZoneProvider tzProvider, IClock clock)
    {
        var now = clock.GetCurrentInstant();

        return tzProvider.Ids
            .Select(id =>
            {
                var zone = tzProvider.GetZoneOrNull(id);
                if (zone is null) return null;

                var offset = zone.GetUtcOffset(now).ToTimeSpan();
                return new TimezoneDto
                {
                    Id = id,
                    DisplayName = $"(UTC{FormatOffset(offset)}) {id}",
                    StandardName = id,
                    BaseUtcOffset = offset,
                    OffsetString = $"UTC{FormatOffset(offset)}",
                    SupportsDaylightSaving = ZoneSupportsDst(zone, now)
                };
            })
            .Where(t => t is not null)
            .Select(t => t!)
            .OrderBy(t => t.BaseUtcOffset)
            .ThenBy(t => t.Id)
            .ToList();
    }

    private static bool ZoneSupportsDst(DateTimeZone zone, Instant now)
    {
        var interval = zone.GetZoneInterval(now);
        var end = interval.HasEnd ? interval.End : now + Duration.FromDays(365);
        var next = zone.GetZoneInterval(end);
        return next.Savings != Offset.Zero || interval.Savings != Offset.Zero;
    }

    private static List<CultureDto> LoadCultures()
    {
        return CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Where(c => !string.IsNullOrEmpty(c.Name) && c.Name.Contains('-'))
            .OrderBy(c => c.DisplayName)
            .Select(c => 
            {
                RegionInfo? region = null;
                try { region = new RegionInfo(c.Name); } catch { }
                
                return new CultureDto
                {
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    NativeName = c.NativeName,
                    DateFormat = c.DateTimeFormat.ShortDatePattern + " " + c.DateTimeFormat.ShortTimePattern,
                    NumberFormat = GetNumberFormatSample(c),
                    CurrencySymbol = c.NumberFormat.CurrencySymbol,
                    Region = region?.EnglishName ?? ""
                };
            })
            .ToList();
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        return $"{sign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}";
    }

    private static string GetNumberFormatSample(CultureInfo culture)
    {
        return 1234567.89m.ToString("N2", culture);
    }

    public IEnumerable<TimezoneDto> GetTimezones()
    {
        return _timezones;
    }

    public TimezoneDto? GetTimezone(string timezoneId)
    {
        return _timezones.FirstOrDefault(tz => 
            tz.Id.Equals(timezoneId, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<CultureDto> GetCultures()
    {
        return _cultures;
    }

    public CultureDto? GetCulture(string cultureName)
    {
        return _cultures.FirstOrDefault(c => 
            c.Name.Equals(cultureName, StringComparison.OrdinalIgnoreCase));
    }

    public DateTime ConvertToTimezone(DateTime utcTime, string timezoneId)
    {
        var zone = _tzProvider.GetZoneOrNull(timezoneId);
        if (zone is null) return utcTime;

        var instant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc));
        return DateTime.SpecifyKind(instant.InZone(zone).ToDateTimeUnspecified(), DateTimeKind.Unspecified);
    }

    public DateTime ConvertToUtc(DateTime localTime, string timezoneId)
    {
        var zone = _tzProvider.GetZoneOrNull(timezoneId);
        if (zone is null) return localTime;

        var local = LocalDateTime.FromDateTime(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified));
        return zone.ResolveLocal(local, DstResolver).ToInstant().ToDateTimeUtc();
    }

    public DateTime GetCurrentTime(string timezoneId)
    {
        return ConvertToTimezone(DateTime.UtcNow, timezoneId);
    }

    public string FormatDateTime(DateTime dateTime, string cultureName, string? format = null)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            return format != null
                ? dateTime.ToString(format, culture)
                : dateTime.ToString("g", culture);
        }
        catch (CultureNotFoundException)
        {
            return dateTime.ToString("g");
        }
    }

    public string FormatNumber(decimal number, string cultureName, int decimals = 2)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            return number.ToString($"N{decimals}", culture);
        }
        catch (CultureNotFoundException)
        {
            return number.ToString($"N{decimals}");
        }
    }
}
