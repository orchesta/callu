using Callu.Shared.Models.StatusPages;

namespace Callu.Application.Services;

/// <summary>
/// Executes health checks against configured component URLs
/// </summary>
public interface IHealthCheckExecutor
{
    /// <summary>
    /// Execute all due health checks (interval elapsed since last check)
    /// </summary>
    Task ExecuteAllChecksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a single health check for a specific component
    /// </summary>
    Task<HealthCheckResultDto> ExecuteSingleCheckAsync(Guid componentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sniff mode: send a test request, capture response, save as sample
    /// </summary>
    Task<HealthCheckSnifferResultDto> SniffAsync(Guid componentId, CancellationToken cancellationToken = default);
}
