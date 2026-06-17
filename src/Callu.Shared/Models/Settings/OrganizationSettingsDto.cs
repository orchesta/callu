namespace Callu.Shared.Models.Settings;

/// <summary>
/// Installation-wide organization settings DTO.
/// </summary>
public record OrganizationSettingsDto
{
    public Guid Id { get; init; }
    public string OrganizationName { get; init; } = "Callu";
    public string DefaultTimezone { get; init; } = "UTC";
    public string DefaultCulture { get; init; } = "en-US";
    public string? BaseUrl { get; init; }
    public bool EmailNotificationsEnabled { get; init; } = true;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
