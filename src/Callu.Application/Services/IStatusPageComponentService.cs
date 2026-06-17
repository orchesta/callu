using Callu.Shared.Models.StatusPages;

namespace Callu.Application.Services;

/// <summary>
/// Manages status page components — CRUD, health check config, and overall status recalculation.
/// Split from StatusPageService for SRP.
/// </summary>
public interface IStatusPageComponentService
{
    /// <summary>
    /// Add a new component to a status page
    /// </summary>
    Task<bool> AddComponentAsync(Guid pageId, AddComponentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing component (status, config, health check settings)
    /// </summary>
    Task<bool> UpdateComponentAsync(Guid componentId, UpdateComponentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove (soft-delete) a component
    /// </summary>
    Task<bool> RemoveComponentAsync(Guid componentId, CancellationToken cancellationToken = default);
}
