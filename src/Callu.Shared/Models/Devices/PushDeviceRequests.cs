using Callu.Domain.Entities;

namespace Callu.Shared.Models.Devices;

public record RegisterPushDeviceRequest
{
    public string Platform { get; init; } = string.Empty;
    public string PushToken { get; init; } = string.Empty;
}

public record UnregisterPushDeviceRequest
{
    public string? PushToken { get; init; }
}

public record PushDeviceDto
{
    public Guid Id { get; init; }
    public string Platform { get; init; } = string.Empty;
    public DateTime LastSeenAt { get; init; }
}

public static class PushPlatforms
{
    public const string Ios = "ios";
    public const string Android = "android";
    public const string Web = "web";

    public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        Ios, Android, Web
    };

    public static string Normalize(string platform) => platform.Trim().ToLowerInvariant();
}

public static class PushDeviceLimits
{
    public const int MaxPushTokenLength = UserPushDevice.MaxPushTokenLength;
    public const int MaxPlatformLength = UserPushDevice.MaxPlatformLength;
}
