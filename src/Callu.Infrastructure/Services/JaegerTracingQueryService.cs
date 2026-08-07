using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Callu.Application.Services;
using Callu.Shared.Models.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

public sealed class JaegerTracingQueryService(
    HttpClient httpClient,
    ILogger<JaegerTracingQueryService> logger) : ITracingQueryService
{
    private const double MicrosecondsPerMillisecond = 1000d;

    private const int MaxLimit = 1000;
    private const int MaxLookbackMinutes = 60 * 24 * 7;

    private const int OverviewSampleSize = 500;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<TracingStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null)
        {
            return new TracingStatusDto(
                Available: false,
                Reason: "no-endpoint",
                Services: []);
        }

        try
        {
            var payload = await GetJsonAsync<JaegerServicesResponse>("api/services", cancellationToken);

            var services = (payload?.Data ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new TracingStatusDto(Available: true, Reason: null, Services: services);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Trace backend at {Endpoint} is not reachable", httpClient.BaseAddress);
            return new TracingStatusDto(Available: false, Reason: "unreachable", Services: []);
        }
    }

    public async Task<IReadOnlyList<TraceSummaryDto>> SearchAsync(
        TraceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null || string.IsNullOrWhiteSpace(request.Service))
            return [];

        var limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var lookback = Math.Clamp(request.LookbackMinutes, 1, MaxLookbackMinutes);

        var end = DateTimeOffset.UtcNow;
        var start = end.AddMinutes(-lookback);

        var query = new List<string>
        {
            $"service={Uri.EscapeDataString(request.Service)}",
            $"limit={(request.OnlyErrors ? Math.Min(limit * 4, MaxLimit * 4) : limit)}",
            $"start={ToMicroseconds(start)}",
            $"end={ToMicroseconds(end)}",
        };

        if (!string.IsNullOrWhiteSpace(request.Operation))
            query.Add($"operation={Uri.EscapeDataString(request.Operation)}");

        if (request.MinDurationMs is > 0)
            query.Add($"minDuration={request.MinDurationMs.Value.ToString(CultureInfo.InvariantCulture)}ms");

        try
        {
            var payload = await GetJsonAsync<JaegerTracesResponse>(
                $"api/traces?{string.Join('&', query)}", cancellationToken);

            var summaries = (payload?.Data ?? [])
                .Select(ToSummary)
                .Where(summary => summary is not null)
                .Select(summary => summary!)
                .Where(summary => !request.OnlyErrors || summary.ErrorCount > 0)
                .OrderByDescending(summary => summary.StartedAt)
                .Take(limit)
                .ToList();

            return summaries;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Trace search against {Endpoint} failed", httpClient.BaseAddress);
            return [];
        }
    }

    public async Task<TracingOverviewDto> GetOverviewAsync(
        TraceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var sample = await SearchAsync(
            request with { Limit = OverviewSampleSize, OnlyErrors = false },
            cancellationToken);

        if (sample.Count == 0)
            return new TracingOverviewDto(0, 0, 0, 0, []);

        var operations = sample
            .GroupBy(trace => (trace.RootOperation, trace.RootService))
            .Select(group =>
            {
                var durations = group.Select(trace => trace.DurationMs).OrderBy(ms => ms).ToList();
                return new OperationStatsDto(
                    Operation: group.Key.RootOperation,
                    Service: group.Key.RootService,
                    TraceCount: group.Count(),
                    AvgDurationMs: durations.Average(),
                    P95DurationMs: Percentile(durations, 0.95),
                    MaxDurationMs: durations[^1],
                    ErrorCount: group.Count(trace => trace.ErrorCount > 0),
                    LastSeen: group.Max(trace => trace.StartedAt));
            })
            .OrderByDescending(op => op.ErrorCount > 0)
            .ThenByDescending(op => op.TraceCount)
            .ToList();

        var allDurations = sample.Select(trace => trace.DurationMs).OrderBy(ms => ms).ToList();

        return new TracingOverviewDto(
            SampleSize: sample.Count,
            TotalErrors: sample.Count(trace => trace.ErrorCount > 0),
            P95DurationMs: Percentile(allDurations, 0.95),
            MaxDurationMs: allDurations[^1],
            Operations: operations);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];

        var rank = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    public async Task<TraceDetailDto?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null || string.IsNullOrWhiteSpace(traceId))
            return null;

        if (!IsHex(traceId))
            return null;

        try
        {
            var payload = await GetJsonAsync<JaegerTracesResponse>($"api/traces/{traceId}", cancellationToken);

            var trace = (payload?.Data ?? []).FirstOrDefault();
            if (trace is null || trace.Spans.Count == 0)
                return null;

            var traceStartMicros = trace.Spans.Min(span => span.StartTime);
            var traceEndMicros = trace.Spans.Max(span => span.StartTime + span.Duration);

            var spans = trace.Spans
                .OrderBy(span => span.StartTime)
                .Select(span => new TraceSpanDto(
                    SpanId: span.SpanId,
                    ParentSpanId: ParentOf(span),
                    Service: ServiceOf(trace, span),
                    Operation: span.OperationName,
                    StartedAt: FromMicroseconds(span.StartTime),
                    StartOffsetMs: (span.StartTime - traceStartMicros) / MicrosecondsPerMillisecond,
                    DurationMs: span.Duration / MicrosecondsPerMillisecond,
                    HasError: HasError(span),
                    Tags: ToTagMap(span)))
                .ToList();

            return new TraceDetailDto(
                TraceId: trace.TraceId,
                StartedAt: FromMicroseconds(traceStartMicros),
                DurationMs: (traceEndMicros - traceStartMicros) / MicrosecondsPerMillisecond,
                Spans: spans);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Fetching trace {TraceId} from {Endpoint} failed", traceId, httpClient.BaseAddress);
            return null;
        }
    }

    private static TraceSummaryDto? ToSummary(JaegerTrace trace)
    {
        if (trace.Spans.Count == 0)
            return null;

        var startMicros = trace.Spans.Min(span => span.StartTime);
        var endMicros = trace.Spans.Max(span => span.StartTime + span.Duration);

        var ids = trace.Spans.Select(span => span.SpanId).ToHashSet(StringComparer.Ordinal);
        var root = trace.Spans.FirstOrDefault(span => ParentOf(span) is not { } parent || !ids.Contains(parent))
                   ?? trace.Spans.OrderBy(span => span.StartTime).First();

        return new TraceSummaryDto(
            TraceId: trace.TraceId,
            RootService: ServiceOf(trace, root),
            RootOperation: root.OperationName,
            StartedAt: FromMicroseconds(startMicros),
            DurationMs: (endMicros - startMicros) / MicrosecondsPerMillisecond,
            SpanCount: trace.Spans.Count,
            ErrorCount: trace.Spans.Count(HasError));
    }

    private static string? ParentOf(JaegerSpan span) =>
        span.References?.FirstOrDefault(reference =>
            string.Equals(reference.RefType, "CHILD_OF", StringComparison.Ordinal))?.SpanId;

    private static string ServiceOf(JaegerTrace trace, JaegerSpan span) =>
        trace.Processes is not null
        && span.ProcessId is not null
        && trace.Processes.TryGetValue(span.ProcessId, out var process)
        && !string.IsNullOrWhiteSpace(process.ServiceName)
            ? process.ServiceName
            : "unknown";

    private static bool HasError(JaegerSpan span)
    {
        if (span.Tags is null)
            return false;

        foreach (var tag in span.Tags)
        {
            var value = tag.Value?.ToString();
            if (string.IsNullOrEmpty(value))
                continue;

            switch (tag.Key)
            {
                case "error" when bool.TryParse(value, out var flagged) && flagged:
                case "otel.status_code" when value.Equals("ERROR", StringComparison.OrdinalIgnoreCase):
                    return true;

                case "http.response.status_code":
                case "http.status_code":
                    if (int.TryParse(value, CultureInfo.InvariantCulture, out var status) && status >= 400)
                        return true;
                    break;
            }
        }

        return false;
    }

    private static Dictionary<string, string> ToTagMap(JaegerSpan span)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in span.Tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(tag.Key))
                continue;

            tags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
        }

        return tags;
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return default;

        return await response.Content.ReadFromJsonAsync<T>(JsonOpts, cancellationToken);
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            var isHexDigit = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHexDigit)
                return false;
        }

        return value.Length is > 0 and <= 64;
    }

    private static long ToMicroseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds() * 1000L;

    private static DateTimeOffset FromMicroseconds(long micros) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)(micros / MicrosecondsPerMillisecond));

    private sealed record JaegerServicesResponse(
        [property: JsonPropertyName("data")] List<string>? Data);

    private sealed record JaegerTracesResponse(
        [property: JsonPropertyName("data")] List<JaegerTrace>? Data);

    private sealed record JaegerTrace(
        [property: JsonPropertyName("traceID")] string TraceId,
        [property: JsonPropertyName("spans")] List<JaegerSpan> Spans,
        [property: JsonPropertyName("processes")] Dictionary<string, JaegerProcess>? Processes);

    private sealed record JaegerSpan(
        [property: JsonPropertyName("spanID")] string SpanId,
        [property: JsonPropertyName("operationName")] string OperationName,
        [property: JsonPropertyName("startTime")] long StartTime,
        [property: JsonPropertyName("duration")] long Duration,
        [property: JsonPropertyName("processID")] string? ProcessId,
        [property: JsonPropertyName("references")] List<JaegerReference>? References,
        [property: JsonPropertyName("tags")] List<JaegerTag>? Tags);

    private sealed record JaegerReference(
        [property: JsonPropertyName("refType")] string RefType,
        [property: JsonPropertyName("spanID")] string SpanId);

    private sealed record JaegerTag(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] object? Value);

    private sealed record JaegerProcess(
        [property: JsonPropertyName("serviceName")] string ServiceName);
}
