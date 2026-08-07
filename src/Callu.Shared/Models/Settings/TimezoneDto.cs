namespace Callu.Shared.Models.Settings;

/// <summary>
/// Timezone information
/// </summary>
public record TimezoneDto
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string StandardName { get; init; } = string.Empty;
    public TimeSpan BaseUtcOffset { get; init; }
    public string OffsetString { get; init; } = string.Empty;
    public bool SupportsDaylightSaving { get; init; }
}