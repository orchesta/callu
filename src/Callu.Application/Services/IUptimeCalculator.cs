namespace Callu.Application.Services;

/// <summary>
/// Computes service uptime % over a window from incident records. Shared by the
/// reporting endpoint (which serves ad-hoc windows) and the daily Quartz job
/// (which keeps the persisted 30-day <c>Service.Uptime</c> figure fresh).
/// </summary>
public interface IUptimeCalculator
{
    /// <summary>
    /// Computes (downtime / total) per service across <paramref name="from"/>..<paramref name="to"/>.
    /// </summary>
    Task<IReadOnlyList<ServiceUptimeResult>> ComputeAsync(
        DateTime from, DateTime to, CancellationToken cancellationToken = default);
}

public sealed record ServiceUptimeResult(
    Guid ServiceId,
    string ServiceName,
    int IncidentCount,
    double TotalDowntimeMinutes,
    double UptimePercent);
