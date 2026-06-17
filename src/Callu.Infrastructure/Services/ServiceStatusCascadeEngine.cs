using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Concrete cascade engine. BFS over the dependency graph with a depth cap of
/// 8 (so an accidentally-deep chain can't burn cycles), edge-criticality
/// matrix for derating, monotonic worsening (never auto-improve — operator
/// must resolve the source incident to clear), and an audit-log emit per
/// touched service.
/// </summary>
public sealed class ServiceStatusCascadeEngine(
    IRepository<ServiceDependency> depRepo,
    IServiceRepository serviceRepo,
    IAuditLogService auditLog,
    ILogger<ServiceStatusCascadeEngine> logger)
    : IServiceStatusCascadeEngine
{
    private const int MaxDepth = 8;

    public async Task<IReadOnlyList<ServiceCascadeOutcome>> PropagateAsync(
        Guid sourceServiceId,
        ServiceStatus newSourceStatus,
        CancellationToken cancellationToken = default)
    {
        if (newSourceStatus == ServiceStatus.Operational)
            return [];

        var edges = await depRepo.GetQueryable()
            .AsNoTracking()
            .Where(d => !d.IsDeleted && d.CascadeStatus)
            .Select(d => new EdgeSnapshot(d.Id, d.ServiceId, d.DependsOnServiceId, d.Criticality, d.CreateIncidentOnFailure))
            .ToListAsync(cancellationToken);

        var bySource = edges
            .GroupBy(e => e.DependsOnServiceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var queue = new Queue<(Guid Node, ServiceStatus Status, int Depth)>();
        queue.Enqueue((sourceServiceId, newSourceStatus, 0));
        var visited = new HashSet<Guid> { sourceServiceId };
        var plan = new List<PlanItem>();

        while (queue.Count > 0)
        {
            var (node, status, depth) = queue.Dequeue();
            if (depth >= MaxDepth)
            {
                logger.LogWarning(
                    "Cascade depth cap ({Max}) reached at {Node}; downstream truncated",
                    MaxDepth, node);
                continue;
            }

            if (!bySource.TryGetValue(node, out var outgoing)) continue;

            foreach (var edge in outgoing)
            {
                if (!visited.Add(edge.ServiceId)) continue;
                var derived = DeriveStatus(edge.Criticality, status);
                if (derived is null) continue;

                plan.Add(new PlanItem(edge.ServiceId, derived.Value, edge.Id, edge.CreateIncidentOnFailure));
                queue.Enqueue((edge.ServiceId, derived.Value, depth + 1));
            }
        }

        if (plan.Count == 0) return [];

        var targets = await serviceRepo.GetQueryable()
            .Where(s => plan.Select(p => p.ServiceId).Contains(s.Id) && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        var touched = new List<ServiceCascadeOutcome>();
        foreach (var item in plan)
        {
            var target = targets.FirstOrDefault(s => s.Id == item.ServiceId);
            if (target is null) continue;

            if (Rank(item.Derived) <= Rank(target.Status)) continue;

            var oldStatus = target.Status;
            target.Status = item.Derived;
            target.UpdatedAt = DateTime.UtcNow;
            touched.Add(new ServiceCascadeOutcome(
                target.Id, target.Name, item.Derived, item.CreateIncidentOnFailure));

            await auditLog.LogAsync(
                userId: null,
                action: "Cascaded",
                entityName: "Service",
                entityId: target.Id.ToString(),
                oldValues: oldStatus.ToString(),
                newValues: $"{item.Derived} (from dep {item.DependencyId})",
                cancellationToken);
        }

        if (touched.Count > 0)
            logger.LogInformation(
                "Cascade from {Source} → {Touched} downstream service(s) worsened",
                sourceServiceId, touched.Count);

        return touched;
    }

    /// <summary>
    /// Criticality → status derating matrix. A "PartialOutage" upstream becomes
    /// MajorOutage to a Critical dependent, PartialOutage to High, Degraded to
    /// Medium, nothing to Low/Optional.
    /// </summary>
    private static ServiceStatus? DeriveStatus(DependencyCriticality criticality, ServiceStatus upstream)
    {
        var effective = upstream == ServiceStatus.UnderMaintenance
            ? ServiceStatus.DegradedPerformance
            : upstream;

        return (criticality, effective) switch
        {
            (DependencyCriticality.Critical, ServiceStatus.MajorOutage) => ServiceStatus.MajorOutage,
            (DependencyCriticality.Critical, ServiceStatus.PartialOutage) => ServiceStatus.MajorOutage,
            (DependencyCriticality.Critical, ServiceStatus.DegradedPerformance) => ServiceStatus.PartialOutage,

            (DependencyCriticality.High, ServiceStatus.MajorOutage) => ServiceStatus.PartialOutage,
            (DependencyCriticality.High, ServiceStatus.PartialOutage) => ServiceStatus.PartialOutage,
            (DependencyCriticality.High, ServiceStatus.DegradedPerformance) => ServiceStatus.DegradedPerformance,

            (DependencyCriticality.Medium, ServiceStatus.MajorOutage) => ServiceStatus.DegradedPerformance,
            (DependencyCriticality.Medium, ServiceStatus.PartialOutage) => ServiceStatus.DegradedPerformance,
            (DependencyCriticality.Medium, ServiceStatus.DegradedPerformance) => ServiceStatus.DegradedPerformance,

            _ => null
        };
    }

    private static int Rank(ServiceStatus s) => s switch
    {
        ServiceStatus.Operational => 0,
        ServiceStatus.DegradedPerformance => 1,
        ServiceStatus.UnderMaintenance => 1,
        ServiceStatus.PartialOutage => 2,
        ServiceStatus.MajorOutage => 3,
        _ => 0
    };

    private record EdgeSnapshot(
        Guid Id,
        Guid ServiceId,
        Guid DependsOnServiceId,
        DependencyCriticality Criticality,
        bool CreateIncidentOnFailure);

    private record PlanItem(
        Guid ServiceId,
        ServiceStatus Derived,
        Guid DependencyId,
        bool CreateIncidentOnFailure);
}
