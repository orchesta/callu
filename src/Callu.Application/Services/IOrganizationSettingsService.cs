using Callu.Shared.Models.Settings;

namespace Callu.Application.Services;

/// <summary>
/// Installation-wide organization settings management.
/// </summary>
public interface IOrganizationSettingsService
{
    /// <summary>
    /// Get current organization settings. Returns a default-populated DTO when no row exists yet.
    /// </summary>
    Task<OrganizationSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save organization settings. Creates the row on first save.
    /// </summary>
    Task<OrganizationSettingsDto> SaveSettingsAsync(UpdateOrganizationSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Resolve the public base URL used for outgoing links (invitation emails, notifications); the result
    /// never has a trailing slash.</summary>
    Task<string> GetPublicBaseUrlAsync(CancellationToken cancellationToken = default);
}
