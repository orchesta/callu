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

        return services.Select(s =>
        {
            var serviceIncidents = incidents.Where(i => i.ServiceId == s.Id).ToList();
            var downtimeMinutes = serviceIncidents.Sum(i =>
            {
                var start = i.CreatedAt < from ? from : i.CreatedAt;
                var end = i.ResolvedAt ?? DateTime.UtcNow;
                if (end > to) end = to;
                return Math.Max(0, (end - start).TotalMinutes);
            });

            return new ServiceUptimeResult(
                ServiceId: s.Id,
                ServiceName: s.Name,
                IncidentCount: serviceIncidents.Count,
                TotalDowntimeMinutes: Math.Round(downtimeMinutes, 1),
                UptimePercent: windowMinutes > 0
                    ? Math.Round((1 - downtimeMinutes / windowMinutes) * 100, 2)
                    : 100);
        })
        .OrderBy(s => s.UptimePercent)
        .ToList();
    }
}
