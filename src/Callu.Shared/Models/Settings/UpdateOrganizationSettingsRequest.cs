namespace Callu.Shared.Models.Settings;

/// <summary>
/// Request to update installation-wide organization settings.
/// </summary>
public record UpdateOrganizationSettingsRequest
{
    public string OrganizationName { get; init; } = "Callu";
    public string DefaultTimezone { get; init; } = "UTC";
    public string DefaultCulture { get; init; } = "en-US";
    public string? BaseUrl { get; init; }
    public bool EmailNotificationsEnabled { get; init; } = true;
}
