using Callu.Shared.Results;
using Callu.Shared.Models.Services;
using Callu.Domain.Enums;

namespace Callu.Application.Services;

/// <summary>
/// Service management and monitoring service interface
/// </summary>
public interface IServiceManagementService
{
    Task<Result<ServiceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<ServiceListDto>>> GetAllAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<ServiceListDto>>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<ServiceListDto>>> GetByStatusAsync(ServiceStatus status, CancellationToken cancellationToken = default);
    Task<Result<ServiceDto>> CreateAsync(CreateServiceRequest dto, CancellationToken cancellationToken = default);
    Task<Result<ServiceDto>> UpdateAsync(Guid id, UpdateServiceRequest dto, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> UpdateStatusAsync(Guid id, ServiceStatus status, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<ServiceDependencyDto>>> GetDependenciesAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<ServiceDependencyDto>>> GetDependentServicesAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<Result<ServiceDependencyDto>> AddDependencyAsync(Guid serviceId, CreateServiceDependencyRequest dto, CancellationToken cancellationToken = default);
    Task<Result> RemoveDependencyAsync(Guid dependencyId, CancellationToken cancellationToken = default);

    Task<Result<double>> GetUptimeAsync(Guid serviceId, int days = 30, CancellationToken cancellationToken = default);

}
