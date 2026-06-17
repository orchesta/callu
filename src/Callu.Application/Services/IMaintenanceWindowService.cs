using Callu.Shared.Models.Maintenance;

namespace Callu.Application.Services;

public interface IMaintenanceWindowService
{
    Task<List<MaintenanceWindowDto>> GetAllAsync(CancellationToken ct = default);
    Task<List<MaintenanceWindowDto>> GetActiveAsync(CancellationToken ct = default);
    Task<MaintenanceWindowDto> CreateAsync(CreateMaintenanceWindowRequest request, string userId, CancellationToken ct = default);
    Task<bool> CancelAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Whether a given service is currently in a maintenance window</summary>
    Task<bool> IsServiceInMaintenanceAsync(Guid serviceId, CancellationToken ct = default);
    /// <summary>Returns the maintenance mode for a service if currently in maintenance, null otherwise</summary>
    Task<string?> GetMaintenanceModeForServiceAsync(Guid serviceId, CancellationToken ct = default);
}
