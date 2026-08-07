using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

public interface IServiceDependencyRepository : IRepository<ServiceDependency>
{
    Task<IEnumerable<ServiceDependency>> GetDependenciesAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ServiceDependency>> GetDependentServicesAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Service>> GetUpstreamServicesAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Service>> GetDownstreamServicesAsync(Guid serviceId, CancellationToken cancellationToken = default);
}
