using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Persistence.Repositories;

public class ServiceDependencyRepository(ApplicationDbContext context, ILogger<ServiceDependencyRepository> logger)
    : Repository<ServiceDependency>(context, logger), IServiceDependencyRepository
{
    public async Task<IEnumerable<ServiceDependency>> GetDependenciesAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(d => d.ServiceId == serviceId)
            .Include(d => d.DependsOnService)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ServiceDependency>> GetDependentServicesAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(d => d.DependsOnServiceId == serviceId)
            .Include(d => d.Service)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Service>> GetUpstreamServicesAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(d => d.ServiceId == serviceId && (d.Type == DependencyType.Upstream || d.Type == DependencyType.Bidirectional))
            .Select(d => d.DependsOnService)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Service>> GetDownstreamServicesAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(d => d.DependsOnServiceId == serviceId && (d.Type == DependencyType.Downstream || d.Type == DependencyType.Bidirectional))
            .Select(d => d.Service)
            .ToListAsync(cancellationToken);
    }
}
