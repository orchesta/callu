namespace Callu.Application.Services;

/// <summary>
/// Service for timezone operations and conversions.
/// All identifiers are IANA Time Zone Database IDs (e.g. "Europe/Istanbul", "America/New_York").
/// Windows-style IDs ("Turkey Standard Time") are NOT accepted; the backend runs in Linux containers
/// and the persistent store uses IANA identifiers exclusively.
/// </summary>
public interface ITimeZoneService
{
    /// <summary>
    /// Convert a UTC <see cref="DateTime"/> to the target IANA timezone.
    /// </summary>
    DateTime ConvertToUserTime(DateTime utcTime, string userTimeZone);

    /// <summary>
    /// Convert a local wall-clock <see cref="DateTime"/> (interpreted in the target IANA zone) to UTC.
    /// DST ambiguity is resolved to the earlier offset; spring-forward gaps shift forward,
    /// matching the rotation materializer's policy.
    /// </summary>
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
