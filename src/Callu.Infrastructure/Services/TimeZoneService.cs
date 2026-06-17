using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using NodaTime;
using NodaTime.TimeZones;
using Callu.Application.Services;
using Callu.Infrastructure.Identity;

namespace Callu.Infrastructure.Services;

/// <summary>
/// IANA timezone service backed by NodaTime's tzdb — platform-independent.
/// Windows TimeZoneInfo is deliberately NOT used: the previous implementation
/// failed silently on Linux containers where "Turkey Standard Time" does not
/// resolve, and schedule materialization / rotation logic already depends on
/// IANA IDs. Using one abstraction everywhere eliminates the split.
/// </summary>
public class TimeZoneService : ITimeZoneService
{
    private static readonly ZoneLocalMappingResolver DstResolver =
        Resolvers.CreateMappingResolver(Resolvers.ReturnEarlier, Resolvers.ReturnForwardShifted);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDateTimeZoneProvider _tzProvider;
    private readonly string _systemDefaultTimeZone;

    public TimeZoneService(
        UserManager<ApplicationUser> userManager,
        IDateTimeZoneProvider tzProvider,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _tzProvider = tzProvider;

        var configured = configuration["SystemSettings:DefaultTimeZone"];
        _systemDefaultTimeZone = !string.IsNullOrWhiteSpace(configured) && tzProvider.GetZoneOrNull(configured) != null
            ? configured
            : "UTC";
    }

    public DateTime ConvertToUserTime(DateTime utcTime, string userTimeZone)
    {
        var zone = _tzProvider.GetZoneOrNull(userTimeZone);
        if (zone is null)
            return DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);

        if (utcTime.Kind != DateTimeKind.Utc)
            utcTime = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);

        var instant = Instant.FromDateTimeUtc(utcTime);
        var zoned = instant.InZone(zone);
        return DateTime.SpecifyKind(zoned.ToDateTimeUnspecified(), DateTimeKind.Unspecified);
    }

    public DateTime ConvertToUtc(DateTime localTime, string userTimeZone)
    {
        var zone = _tzProvider.GetZoneOrNull(userTimeZone);
        if (zone is null)
            return DateTime.SpecifyKind(localTime, DateTimeKind.Utc);

        var local = LocalDateTime.FromDateTime(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified));
        var instant = zone.ResolveLocal(local, DstResolver).ToInstant();
        return instant.ToDateTimeUtc();
    }

    public async Task<string> GetUserTimeZoneAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        var userTz = user?.Timezone;
        if (!string.IsNullOrWhiteSpace(userTz) && _tzProvider.GetZoneOrNull(userTz) != null)
            return userTz;
        return _systemDefaultTimeZone;
    }

    public string GetSystemDefaultTimeZone() => _systemDefaultTimeZone;

    public IReadOnlyCollection<string> GetAvailableTimeZones() => _tzProvider.Ids;

    public bool IsValidTimeZone(string timezoneId) =>
        !string.IsNullOrWhiteSpace(timezoneId) && _tzProvider.GetZoneOrNull(timezoneId) != null;
}
