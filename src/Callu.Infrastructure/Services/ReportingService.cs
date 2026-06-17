using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Read-only reporting service that computes analytics from incident data.
/// </summary>
public class ReportingService(
    IRepository<Incident> incidentRepo,
    IRepository<Team> teamRepo,
    IUptimeCalculator uptimeCalculator,
    HybridCache cache) : IReportingService
{
    private static readonly HybridCacheEntryOptions ReportCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };

    private static string ReportKey(string type, DateTime from, DateTime to, string? extra = null) =>
        $"report:{type}:{from:yyyyMMdd}:{to:yyyyMMdd}{(extra != null ? $":{extra}" : "")}";
    public async Task<List<IncidentTrendPointDto>> GetIncidentTrendsAsync(
        DateTime from, DateTime to, string groupBy = "day", CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            ReportKey("trends", from, to, groupBy),
            async innerCt => await BuildIncidentTrendsAsync(from, to, groupBy, innerCt),
            ReportCacheOptions,
            cancellationToken: ct);
    }

    private async Task<List<IncidentTrendPointDto>> BuildIncidentTrendsAsync(
        DateTime from, DateTime to, string groupBy, CancellationToken ct)
    {
        var incidents = await incidentRepo.GetQueryable()
            .Where(i => i.CreatedAt >= from && i.CreatedAt <= to)
            .Select(i => new { i.CreatedAt, i.Severity })
            .ToListAsync(ct);

        var grouped = groupBy.ToLowerInvariant() switch
        {
            "week" => incidents.GroupBy(i => new DateTime(i.CreatedAt.Year, 1, 1).AddDays((i.CreatedAt.DayOfYear - 1) / 7 * 7)),
            "month" => incidents.GroupBy(i => new DateTime(i.CreatedAt.Year, i.CreatedAt.Month, 1)),
            _ => incidents.GroupBy(i => i.CreatedAt.Date)
        };

        return grouped
            .Select(g => new IncidentTrendPointDto
            {
                Date = g.Key,
                Count = g.Count(),
                Critical = g.Count(i => i.Severity == IncidentSeverity.Critical),
                High = g.Count(i => i.Severity == IncidentSeverity.High),
                Medium = g.Count(i => i.Severity == IncidentSeverity.Medium),
                Low = g.Count(i => i.Severity == IncidentSeverity.Low),
            })
            .OrderBy(p => p.Date)
            .ToList();
    }

    public async Task<List<MttMetricPointDto>> GetMttMetricsAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            ReportKey("mtt", from, to),
            async innerCt => await BuildMttMetricsAsync(from, to, innerCt),
            ReportCacheOptions,
            cancellationToken: ct);
    }

    private async Task<List<MttMetricPointDto>> BuildMttMetricsAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var incidents = await incidentRepo.GetQueryable()
            .Where(i => i.CreatedAt >= from && i.CreatedAt <= to)
            .Select(i => new { i.CreatedAt, i.AcknowledgedAt, i.ResolvedAt })
            .ToListAsync(ct);

        var byWeek = incidents.GroupBy(i =>
            new DateTime(i.CreatedAt.Year, 1, 1).AddDays((i.CreatedAt.DayOfYear - 1) / 7 * 7));

        return byWeek
            .Select(g =>
            {
                var acked = g.Where(i => i.AcknowledgedAt.HasValue).ToList();
                var resolved = g.Where(i => i.ResolvedAt.HasValue).ToList();

                return new MttMetricPointDto
                {
                    Date = g.Key,
                    IncidentCount = g.Count(),
                    MttaMinutes = acked.Count > 0
                        ? acked.Average(i => (i.AcknowledgedAt!.Value - i.CreatedAt).TotalMinutes)
                        : 0,
                    MttrMinutes = resolved.Count > 0
                        ? resolved.Average(i => (i.ResolvedAt!.Value - i.CreatedAt).TotalMinutes)
                        : 0,
                };
            })
            .OrderBy(p => p.Date)
            .ToList();
    }

    public async Task<List<ServiceUptimeDto>> GetServiceUptimeAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            ReportKey("uptime", from, to),
            async innerCt => await BuildServiceUptimeAsync(from, to, innerCt),
            ReportCacheOptions,
            cancellationToken: ct);
    }

    private async Task<List<ServiceUptimeDto>> BuildServiceUptimeAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await uptimeCalculator.ComputeAsync(from, to, ct);
        return rows.Select(r => new ServiceUptimeDto
        {
            ServiceId = r.ServiceId,
            ServiceName = r.ServiceName,
            IncidentCount = r.IncidentCount,
            TotalDowntimeMinutes = r.TotalDowntimeMinutes,
            UptimePercent = r.UptimePercent,
        }).ToList();
    }

    public async Task<List<TeamPerformanceDto>> GetTeamPerformanceAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            ReportKey("team-perf", from, to),
            async innerCt => await BuildTeamPerformanceAsync(from, to, innerCt),
            ReportCacheOptions,
            cancellationToken: ct);
    }

    private async Task<List<TeamPerformanceDto>> BuildTeamPerformanceAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var teams = await teamRepo.GetQueryable()
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);

        var incidents = await incidentRepo.GetQueryable()
            .Where(i => i.CreatedAt >= from && i.CreatedAt <= to && i.TeamId.HasValue)
            .Select(i => new { i.TeamId, i.CreatedAt, i.AcknowledgedAt, i.ResolvedAt })
            .ToListAsync(ct);

        return teams.Select(t =>
        {
            var teamIncidents = incidents.Where(i => i.TeamId == t.Id).ToList();
            var acked = teamIncidents.Where(i => i.AcknowledgedAt.HasValue).ToList();
            var resolved = teamIncidents.Where(i => i.ResolvedAt.HasValue).ToList();

            return new TeamPerformanceDto
            {
                TeamId = t.Id,
                TeamName = t.Name,
                TotalIncidents = teamIncidents.Count,
                ResolvedCount = resolved.Count,
                AvgAcknowledgeMinutes = acked.Count > 0
                    ? Math.Round(acked.Average(i => (i.AcknowledgedAt!.Value - i.CreatedAt).TotalMinutes), 1)
                    : 0,
                AvgResolveMinutes = resolved.Count > 0
                    ? Math.Round(resolved.Average(i => (i.ResolvedAt!.Value - i.CreatedAt).TotalMinutes), 1)
                    : 0,
            };
        })
        .OrderByDescending(t => t.TotalIncidents)
        .ToList();
    }

    public async Task<List<SeverityDistributionDto>> GetSeverityDistributionAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            ReportKey("severity-dist", from, to),
            async innerCt => await BuildSeverityDistributionAsync(from, to, innerCt),
            ReportCacheOptions,
            cancellationToken: ct);
    }

    private async Task<List<SeverityDistributionDto>> BuildSeverityDistributionAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var incidents = await incidentRepo.GetQueryable()
            .Where(i => i.CreatedAt >= from && i.CreatedAt <= to)
            .GroupBy(i => i.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total = incidents.Sum(i => i.Count);

        return incidents.Select(i => new SeverityDistributionDto
        {
            Severity = i.Severity.ToString(),
            Count = i.Count,
            Percentage = total > 0 ? Math.Round((double)i.Count / total * 100, 1) : 0,
        })
        .OrderByDescending(i => i.Count)
        .ToList();
    }
}
