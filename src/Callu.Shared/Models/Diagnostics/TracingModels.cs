namespace Callu.Shared.Models.Diagnostics;

public sealed record TracingStatusDto(
    bool Available,
    string? Reason,
    IReadOnlyList<string> Services);

public sealed record TraceSummaryDto(
    string TraceId,
    string RootService,
    string RootOperation,
    DateTimeOffset StartedAt,
    double DurationMs,
    int SpanCount,
    int ErrorCount);

public sealed record TraceSpanDto(
    string SpanId,
    string? ParentSpanId,
    string Service,
    string Operation,
    DateTimeOffset StartedAt,
    double StartOffsetMs,
    double DurationMs,
    bool HasError,
    IReadOnlyDictionary<string, string> Tags);

public sealed record TraceDetailDto(
    string TraceId,
    DateTimeOffset StartedAt,
    double DurationMs,
    IReadOnlyList<TraceSpanDto> Spans);

public sealed record OperationStatsDto(
    string Operation,
    string Service,
    int TraceCount,
    double AvgDurationMs,
    double P95DurationMs,
    double MaxDurationMs,
    int ErrorCount,
    DateTimeOffset LastSeen);

public sealed record TracingOverviewDto(
    int SampleSize,
    int TotalErrors,
    double P95DurationMs,
    double MaxDurationMs,
    IReadOnlyList<OperationStatsDto> Operations);

public sealed record TraceSearchRequest
{
    public string? Service { get; init; }
    public string? Operation { get; init; }
    public int LookbackMinutes { get; init; } = 60;
    public int Limit { get; init; } = 50;
    public bool OnlyErrors { get; init; }
    public int? MinDurationMs { get; init; }
}
