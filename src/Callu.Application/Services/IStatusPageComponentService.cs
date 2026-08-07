using Callu.Shared.Models.StatusPages;
using Callu.Domain.Enums;

namespace Callu.Application.Services;

/// <summary>
/// Manages status page components — CRUD, health check config, and overall status recalculation.
/// Split from StatusPageService for SRP.
/// </summary>
public interface IStatusPageComponentService
{
    Task<bool> AddComponentAsync(Guid pageId, AddComponentRequest request, CancellationToken cancellationToken = default);

    Task<bool> UpdateComponentAsync(Guid componentId, UpdateComponentRequest request, CancellationToken cancellationToken = default);

    Task<bool> RemoveComponentAsync(Guid componentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mirrors service statuses onto linked components that are not health-check owned.
    /// </summary>
    Task SyncFromServiceStatusesAsync(
        IReadOnlyList<(Guid ServiceId, ServiceStatus Status)> updates,
        CancellationToken cancellationToken = default);
}
