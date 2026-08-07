using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Callu.Infrastructure.Services;

public sealed class UptimeCalculator(
    IServiceRepository serviceRepo,
    IIncidentRepository incidentRepo)
    : IUptimeCalculator
{
    public async Task<IReadOnlyList<ServiceUptimeResult>> ComputeAsync(
        DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var services = await serviceRepo.GetQueryable()
            .AsNoTracking()
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken);

        var incidents = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .Where(i => i.ServiceId.HasValue
                        && i.CreatedAt < to
                        && (i.ResolvedAt == null || i.ResolvedAt >= from))
            .Select(i => new { i.ServiceId, i.CreatedAt, i.ResolvedAt })
            .ToListAsync(cancellationToken);

        var windowMinutes = (to - from).TotalMinutes;
        var now = DateTime.UtcNow;

        return services.Select(s =>
        {
            var serviceIncidents = incidents.Where(i => i.ServiceId == s.Id).ToList();
            var intervals = serviceIncidents
                .Select(i =>
                {
                    var start = i.CreatedAt < from ? from : i.CreatedAt;
                    var end = i.ResolvedAt ?? now;
                    if (end > to) end = to;
                    return (Start: start, End: end);
                })
                .Where(x => x.End > x.Start)
                .OrderBy(x => x.Start);

            var downtimeMinutes = 0d;
            DateTime? mergedStart = null;
            var mergedEnd = DateTime.MinValue;
            foreach (var (start, end) in intervals)
            {
                if (mergedStart is null)
                {
                    mergedStart = start;
                    mergedEnd = end;
                }
                else if (start <= mergedEnd)
                {
                    if (end > mergedEnd) mergedEnd = end;
                }
                else
                {
                    downtimeMinutes += (mergedEnd - mergedStart.Value).TotalMinutes;
                    mergedStart = start;
                    mergedEnd = end;
                }
            }
            if (mergedStart is not null)
                downtimeMinutes += (mergedEnd - mergedStart.Value).TotalMinutes;

            return new ServiceUptimeResult(
                ServiceId: s.Id,
                ServiceName: s.Name,
                IncidentCount: serviceIncidents.Count,
                TotalDowntimeMinutes: Math.Round(downtimeMinutes, 1),
                UptimePercent: windowMinutes > 0
                    ? Math.Round(Math.Clamp((1 - downtimeMinutes / windowMinutes) * 100, 0, 100), 2)
                    : 100);
        })
        .OrderBy(s => s.UptimePercent)
        .ToList();
    }
}
