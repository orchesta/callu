using Callu.Shared.Models.Services;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for Service Catalog management
/// </summary>
public interface IServiceCatalogService
{
    /// <summary>
    /// Get all services
    /// </summary>
    Task<IEnumerable<ServiceDto>> GetServicesAsync(CancellationToken cancellationToken = default);
}

