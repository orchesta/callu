using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Mapster;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Shared.Models.Incidents;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Read-only incident query service — handles dashboard summaries,
/// status counts, and external alert lookups.
/// Extracted from IncidentService for ISP compliance.
/// </summary>
public class IncidentQueryService(
    IIncidentRepository incidentRepo,
    IServiceRepository serviceRepo,
    HybridCache cache) : IIncidentQueryService
{
    private static readonly HybridCacheEntryOptions DashboardCacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(15)
    };
    public async Task<Dictionary<string, int>> GetIncidentCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .Where(i => !i.IsDeleted)
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        
        return counts.ToDictionary(x => x.Status.ToString(), x => x.Count);
    }

    public async Task<Shared.Models.Dashboard.DashboardSummaryDto> GetDashboardSummaryAsync(int recentCount = 5, int timeRangeDays = 0, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"dashboard-summary:{recentCount}:{timeRangeDays}",
            async ct => await BuildDashboardSummaryAsync(recentCount, timeRangeDays, ct),
            DashboardCacheOptions,
            cancellationToken: cancellationToken);
    }

    private async Task<Shared.Models.Dashboard.DashboardSummaryDto> BuildDashboardSummaryAsync(int recentCount, int timeRangeDays, CancellationToken cancellationToken)
    {
        var baseQuery = incidentRepo.GetQueryable()
            .AsNoTracking()
            .Where(i => !i.IsDeleted);

        if (timeRangeDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-timeRangeDays);
            baseQuery = baseQuery.Where(i => i.StartedAt >= cutoff);
        }

        var activeIncidents = baseQuery;

        var statusCounts = await activeIncidents
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var triggeredCount = statusCounts
            .Where(s => s.Status == IncidentStatus.Open)
            .Sum(s => s.Count);
        var acknowledgedCount = statusCounts
            .FirstOrDefault(s => s.Status == IncidentStatus.Acknowledged)?.Count ?? 0;
        var resolvedCount = statusCounts
            .FirstOrDefault(s => s.Status == IncidentStatus.Resolved)?.Count ?? 0;
        var totalIncidents = statusCounts.Sum(s => s.Count);

        var severityCounts = await activeIncidents
            .GroupBy(i => i.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var severityDict = severityCounts.ToDictionary(x => x.Severity.ToString(), x => x.Count);
        var criticalCount = severityCounts
            .FirstOrDefault(s => s.Severity == IncidentSeverity.Critical)?.Count ?? 0;

        var metricsCutoff = DateTime.UtcNow.AddDays(-90);
        var ackTimes = await activeIncidents
            .Where(i => i.AcknowledgedAt.HasValue && i.StartedAt >= metricsCutoff)
            .Select(i => new { i.StartedAt, AckedAt = i.AcknowledgedAt!.Value })
            .ToListAsync(cancellationToken);
        var mttaMinutes = ackTimes.Count > 0
            ? ackTimes.Average(t => (t.AckedAt - t.StartedAt).TotalMinutes)
            : 0;

        var resolveTimes = await activeIncidents
            .Where(i => i.ResolvedAt.HasValue && i.StartedAt >= metricsCutoff)
            .Select(i => new { i.StartedAt, ResolvedAtVal = i.ResolvedAt!.Value })
            .ToListAsync(cancellationToken);
        var mttrMinutes = resolveTimes.Count > 0
            ? resolveTimes.Average(t => (t.ResolvedAtVal - t.StartedAt).TotalMinutes)
            : 0;

        var recentIncidents = await activeIncidents
            .Include(i => i.Service)
            .Include(i => i.Team)
            .OrderByDescending(i => i.StartedAt)
            .Take(recentCount)
            .Select(i => new IncidentListItemDto
            {
                Id = i.Id,
                Title = i.Title,
                Severity = i.Severity.ToString(),
                Status = i.Status.ToString(),
                StartedAt = i.StartedAt,
                AcknowledgedAt = i.AcknowledgedAt,
                ResolvedAt = i.ResolvedAt,
                ServiceName = i.Service != null ? i.Service.Name : null,
                TeamName = i.Team != null ? i.Team.Name : null
            })
            .ToListAsync(cancellationToken);

        var services = await serviceRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Select(s => s.Adapt<Shared.Models.Services.ServiceDto>())
            .ToListAsync(cancellationToken);

        var resolvedRate = totalIncidents > 0 ? (resolvedCount * 100 / totalIncidents) : 100;

        return new Shared.Models.Dashboard.DashboardSummaryDto
        {
            TriggeredCount = triggeredCount,
            AcknowledgedCount = acknowledgedCount,
            ResolvedCount = resolvedCount,
            TotalIncidents = totalIncidents,
            CriticalCount = criticalCount,
            ResolvedRate = resolvedRate,
            Mtta = FormatDuration(mttaMinutes),
            Mttr = FormatDuration(mttrMinutes),
            SeverityCounts = severityDict,
            RecentIncidents = recentIncidents,
            TotalServices = services.Count,
            Services = services
        };
    }

    public async Task<IncidentDto?> FindByExternalAlertIdAsync(string externalAlertId, CancellationToken cancellationToken = default)
    {
        var incident = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .Include(i => i.Service)
            .Include(i => i.Team)
            .Where(i => !i.IsDeleted && i.ExternalAlertId == externalAlertId && i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed)
            .OrderByDescending(i => i.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        
        return incident?.Adapt<IncidentDto>();
    }

    private static string FormatDuration(double minutes)
    {
        if (minutes < 60) return $"{(int)minutes}m";
        if (minutes < 1440) return $"{(int)(minutes / 60)}h {(int)(minutes % 60)}m";
        return $"{(int)(minutes / 1440)}d";
    }
}
