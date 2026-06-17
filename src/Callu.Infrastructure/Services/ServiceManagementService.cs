using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Shared.Results;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Shared.Models.Services;
using Callu.Shared.Models.Incidents;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Unified implementation of service management, monitoring and health tracking
/// </summary>
public class ServiceManagementService(
    IServiceRepository serviceRepo,
    IServiceDependencyRepository depRepo,
    ITransactionManager transactionManager,
    IAuditLogService auditLogService,
    IServiceStatusCascadeEngine cascadeEngine,
    IUptimeCalculator uptimeCalculator,
    IIncidentService incidentService,
    ILogger<ServiceManagementService> logger) : IServiceManagementService
{
    public async Task<Result<ServiceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var service = await serviceRepo.GetQueryable()
            .Include(s => s.Team)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);

        if (service == null)
            return Result.Failure<ServiceDto>("Service not found");

        return Result.Success(service.Adapt<ServiceDto>());
    }

    public async Task<Result<PagedResult<ServiceListDto>>> GetAllAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var query = serviceRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Name);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(s => s.Adapt<ServiceListDto>()).ToList();
        return Result.Success(new PagedResult<ServiceListDto>(dtos, total, page, pageSize));
    }

    public async Task<Result<IEnumerable<ServiceListDto>>> GetByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var services = await serviceRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => s.TeamId == teamId && !s.IsDeleted)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return Result.Success(services.Select(s => s.Adapt<ServiceListDto>()));
    }

    public async Task<Result<IEnumerable<ServiceListDto>>> GetByStatusAsync(ServiceStatus status, CancellationToken cancellationToken = default)
    {
        var services = await serviceRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => s.Status == status && !s.IsDeleted)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return Result.Success(services.Select(s => s.Adapt<ServiceListDto>()));
    }

    public async Task<Result<ServiceDto>> CreateAsync(CreateServiceRequest dto, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = dto.Adapt<Service>();
            service.Id = Guid.NewGuid();
            service.CreatedAt = DateTime.UtcNow;
            service.Status = ServiceStatus.Operational;

            await serviceRepo.AddAsync(service, cancellationToken);
            
            await auditLogService.LogAsync(null, "Created", "Service", service.Id.ToString(), null, System.Text.Json.JsonSerializer.Serialize(dto), cancellationToken);

            return Result.Success(service.Adapt<ServiceDto>());
        }, cancellationToken);
    }

    public async Task<Result<ServiceDto>> UpdateAsync(Guid id, UpdateServiceRequest dto, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ServiceCascadeOutcome> cascade = [];

        var result = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);
            if (service == null) return Result.Failure<ServiceDto>("Service not found");

            var oldValues = System.Text.Json.JsonSerializer.Serialize(service.Adapt<ServiceDto>());
            var oldStatus = service.Status;

            var originalAckMethod = service.AckHttpMethod ?? "POST";
            var originalAckCType = service.AckContentType ?? "application/json";

            dto.Adapt(service);

            service.AckHttpMethod ??= originalAckMethod;
            service.AckContentType ??= originalAckCType;

            service.UpdatedAt = DateTime.UtcNow;

            await auditLogService.LogAsync(null, "Updated", "Service", id.ToString(), oldValues, System.Text.Json.JsonSerializer.Serialize(dto), cancellationToken);

            if (service.Status != oldStatus)
                cascade = await cascadeEngine.PropagateAsync(id, service.Status, cancellationToken);

            return Result.Success(service.Adapt<ServiceDto>());
        }, cancellationToken);

        if (result.IsSuccess)
            await CreateCascadeIncidentsAsync(cascade, cancellationToken);

        return result;
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);
            if (service == null) return Result.Failure("Service not found");

            service.IsDeleted = true;
            service.UpdatedAt = DateTime.UtcNow;

            await auditLogService.LogAsync(null, "Deleted", "Service", id.ToString(), null, null, cancellationToken);

            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result> UpdateStatusAsync(Guid id, ServiceStatus status, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ServiceCascadeOutcome> cascade = [];

        var result = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == id && !s.IsDeleted, cancellationToken);
            if (service == null) return Result.Failure("Service not found");

            var oldStatus = service.Status;
            service.Status = status;
            service.UpdatedAt = DateTime.UtcNow;

            await auditLogService.LogAsync(null, "Updated", "Service", id.ToString(), $"Status: {oldStatus}", $"Status: {status}", cancellationToken);

            cascade = await cascadeEngine.PropagateAsync(id, status, cancellationToken);

            return Result.Success();
        }, cancellationToken);

        if (result.IsSuccess)
            await CreateCascadeIncidentsAsync(cascade, cancellationToken);

        return result;
    }

    /// <summary>
    /// Opens an incident for each cascaded service whose dependency edge has
    /// CreateIncidentOnFailure set. Runs AFTER the status transaction commits so each
    /// incident gets its own transaction + escalation dispatch and a failed incident
    /// can't roll back the status change. ExternalAlertId dedupes re-runs while an
    /// auto-opened incident for the same service is still active.
    /// </summary>
    private async Task CreateCascadeIncidentsAsync(
        IReadOnlyList<ServiceCascadeOutcome> cascade, CancellationToken cancellationToken)
    {
        foreach (var outcome in cascade)
        {
            if (!outcome.ShouldCreateIncident) continue;

            try
            {
                await incidentService.CreateIncidentAsync(new CreateIncidentRequest
                {
                    Title = $"{outcome.ServiceName} degraded by upstream dependency",
                    Description = $"Automatically opened: a dependency failure cascaded {outcome.ServiceName} to {outcome.NewStatus}.",
                    Severity = MapSeverity(outcome.NewStatus),
                    ServiceId = outcome.ServiceId,
                    ExternalAlertId = $"cascade:{outcome.ServiceId}"
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to auto-create cascade incident for service {ServiceId}", outcome.ServiceId);
            }
        }
    }

    private static string MapSeverity(ServiceStatus status) => status switch
    {
        ServiceStatus.MajorOutage => "Critical",
        ServiceStatus.PartialOutage => "High",
        _ => "Medium"
    };

    public async Task<Result<IEnumerable<ServiceDependencyDto>>> GetDependenciesAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        var deps = await depRepo.GetQueryable()
            .Include(d => d.Service)
            .Include(d => d.DependsOnService)
            .AsNoTracking()
            .Where(d => d.ServiceId == serviceId && !d.IsDeleted)
            .ToListAsync(cancellationToken);

        return Result.Success(deps.Select(d => d.Adapt<ServiceDependencyDto>()));
    }

    public async Task<Result<IEnumerable<ServiceDependencyDto>>> GetDependentServicesAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        var deps = await depRepo.GetQueryable()
            .Include(d => d.Service)
            .Include(d => d.DependsOnService)
            .AsNoTracking()
            .Where(d => d.DependsOnServiceId == serviceId && !d.IsDeleted)
            .ToListAsync(cancellationToken);

        return Result.Success(deps.Select(d => d.Adapt<ServiceDependencyDto>()));
    }

    public async Task<Result<ServiceDependencyDto>> AddDependencyAsync(Guid serviceId, CreateServiceDependencyRequest dto, CancellationToken cancellationToken = default)
    {
        if (serviceId == dto.DependsOnServiceId)
            return Result.Failure<ServiceDependencyDto>("A service cannot depend on itself.");

        var alreadyExists = await depRepo.GetQueryable()
            .AsNoTracking()
            .AnyAsync(d => d.ServiceId == serviceId
                           && d.DependsOnServiceId == dto.DependsOnServiceId
                           && !d.IsDeleted, cancellationToken);
        if (alreadyExists)
            return Result.Failure<ServiceDependencyDto>("This dependency already exists.");

        if (await IntroducesCycleAsync(serviceId, dto.DependsOnServiceId, cancellationToken))
            return Result.Failure<ServiceDependencyDto>("Adding this dependency would create a cycle in the service graph.");

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var dep = dto.Adapt<ServiceDependency>();
            dep.Id = Guid.NewGuid();
            dep.ServiceId = serviceId;
            dep.CreatedAt = DateTime.UtcNow;

            await depRepo.AddAsync(dep, cancellationToken);
            return Result.Success(dep.Adapt<ServiceDependencyDto>());
        }, cancellationToken);
    }

    /// <summary>
    /// Tests whether adding the proposed edge <paramref name="fromService"/> →
    /// <paramref name="toService"/> (i.e. fromService depends on toService) would close a
    /// cycle. It does so by walking existing edges from <paramref name="toService"/> and
    /// checking whether <paramref name="fromService"/> is already reachable. Iterative DFS so
    /// a maliciously deep graph doesn't stack-overflow.
    /// </summary>
    private async Task<bool> IntroducesCycleAsync(Guid fromService, Guid toService, CancellationToken cancellationToken)
    {
        var edges = await depRepo.GetQueryable()
            .AsNoTracking()
            .Where(d => !d.IsDeleted)
            .Select(d => new { d.ServiceId, d.DependsOnServiceId })
            .ToListAsync(cancellationToken);

        var adjacency = edges
            .GroupBy(e => e.ServiceId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.DependsOnServiceId).ToArray());

        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(toService);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == fromService) return true;
            if (!visited.Add(node)) continue;
            if (!adjacency.TryGetValue(node, out var neighbours)) continue;
            foreach (var n in neighbours)
                if (!visited.Contains(n))
                    stack.Push(n);
        }

        return false;
    }

    public async Task<Result> RemoveDependencyAsync(Guid dependencyId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var dep = await depRepo.FindSingleAsync(d => d.Id == dependencyId && !d.IsDeleted, cancellationToken);
            if (dep == null) return Result.Failure("Dependency not found");

            dep.IsDeleted = true;
            dep.UpdatedAt = DateTime.UtcNow;
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result<double>> GetUptimeAsync(Guid serviceId, int days = 30, CancellationToken cancellationToken = default)
    {
        var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
        if (service == null) return Result.Failure<double>("Service not found");

        var to = DateTime.UtcNow;
        var from = to.AddDays(-Math.Max(1, days));
        var results = await uptimeCalculator.ComputeAsync(from, to, cancellationToken);
        var match = results.FirstOrDefault(r => r.ServiceId == serviceId);

        return Result.Success(match?.UptimePercent ?? 100.0);
    }
}
