using Callu.Shared.Models.StatusPages;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for Status Page operations
/// </summary>
public interface IStatusPageService
{
    Task<IEnumerable<StatusPageDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<StatusPageDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StatusPageDetailDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    /// <summary>Public-safe: bypasses tenant filter, only returns IsPublic pages (for anonymous visitors).
    /// Returns the trimmed PublicStatusPageDto so health-check URLs/secrets never reach the wire.</summary>
    Task<PublicStatusPageDto?> GetBySlugPublicAsync(string slug, CancellationToken cancellationToken = default);
    Task<StatusPageDto> CreateAsync(CreateStatusPageRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Guid id, UpdateStatusPageRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<StatusPageIncidentDto?> CreateIncidentAsync(Guid pageId, CreateStatusIncidentRequest request, CancellationToken cancellationToken = default);
    Task<bool> AddIncidentUpdateAsync(Guid incidentId, AddIncidentUpdateRequest request, CancellationToken cancellationToken = default);

    Task<StatusPageStatsDto> GetStatsAsync(Guid pageId, CancellationToken cancellationToken = default);

    /// <summary>Returns 30-day per-component uptime history for a status page.</summary>
    Task<IEnumerable<ComponentUptimeDto>> GetUptimeAsync(Guid pageId, int days, CancellationToken cancellationToken = default);

    Task RecordPageViewAsync(Guid pageId, string? visitorHash, CancellationToken cancellationToken = default);

    /// <summary>Triggers double opt-in: stores unconfirmed row, mails confirmation link.</summary>
    Task<bool> SubscribeAsync(Guid pageId, string email, CancellationToken cancellationToken = default);

    /// <summary>Flips IsConfirmed if the plaintext token matches a stored hash and isn't expired.</summary>
    Task<bool> ConfirmSubscriptionAsync(string token, CancellationToken cancellationToken = default);

    Task<bool> UnsubscribeAsync(Guid pageId, string email, CancellationToken cancellationToken = default);

    /// <summary>One-click unsubscribe via the token embedded in every notification email.</summary>
    Task<bool> UnsubscribeByTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<IEnumerable<StatusPageSubscriberDto>> GetSubscribersAsync(Guid pageId, CancellationToken cancellationToken = default);
}
