using Callu.Shared.Models.Diagnostics;

namespace Callu.Application.Services;

public interface ITracingQueryService
{
    Task<TracingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TraceSummaryDto>> SearchAsync(TraceSearchRequest request, CancellationToken cancellationToken = default);

    Task<TracingOverviewDto> GetOverviewAsync(TraceSearchRequest request, CancellationToken cancellationToken = default);

    Task<TraceDetailDto?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default);
}
