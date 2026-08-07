using Microsoft.EntityFrameworkCore;
using Mapster;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Shared.Models.Services;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Service Catalog service implementation
/// </summary>
public class ServiceCatalogService(
    IServiceRepository serviceRepo,
    IIncidentRepository incidentRepo) : IServiceCatalogService
{
    public async Task<IEnumerable<ServiceDto>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        var services = await serviceRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Include(s => s.Team)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var incidentCounts = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.ServiceId != null)
            .GroupBy(i => i.ServiceId!.Value)
            .Select(g => new { ServiceId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var countByService = incidentCounts.ToDictionary(x => x.ServiceId, x => x.Count);

        return services.Select(s =>
        {
            var dto = s.Adapt<ServiceDto>();
            return dto with
            {
                Type = s.Type.ToString(),
                IncidentCount = countByService.TryGetValue(s.Id, out var count) ? count : 0
            };
        });
    }
}
