namespace Callu.Application.Services;

/// <summary>Timezone operations and conversions. Every identifier is an IANA Time Zone Database ID
/// (e.g. "Europe/Istanbul"); Windows-style names are not accepted.</summary>
public interface ITimeZoneService
{
    /// <summary>
    /// Convert a UTC <see cref="DateTime"/> to the target IANA timezone.
    /// </summary>
    DateTime ConvertToUserTime(DateTime utcTime, string userTimeZone);

    /// <summary>Convert a local wall-clock <see cref="DateTime"/> (interpreted in the target IANA zone) to UTC,
    /// using the same DST resolution as the rotation materializer.</summary>
    DateTime ConvertToUtc(DateTime localTime, string userTimeZone);

    /// <summary>
    /// Lookup the user's configured IANA timezone, falling back to the system default.
    /// </summary>
    Task<string> GetUserTimeZoneAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// IANA timezone ID used as the system default when no user/entity timezone is set.
    /// </summary>
    string GetSystemDefaultTimeZone();

    /// <summary>
    /// IANA timezone IDs available to the frontend for pickers.
    /// </summary>
    IReadOnlyCollection<string> GetAvailableTimeZones();

    /// <summary>
    /// True if <paramref name="timezoneId"/> is a recognised IANA zone.
    /// </summary>
    bool IsValidTimeZone(string timezoneId);
}
