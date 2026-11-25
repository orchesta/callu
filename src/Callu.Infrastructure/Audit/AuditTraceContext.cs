using System.Diagnostics;

namespace Callu.Infrastructure.Audit;

/// <summary>The trace and span the audited operation ran under, taken from the active context.</summary>
// Identifiers are read from the ambient Activity, never minted here: one generated for the audit row
// is well-formed and matches no span anywhere. Reading them is deliberately independent of whether
// the trace is sampled — an audit trail that appears only for sampled requests is not a record.
internal static class AuditTraceContext
{
    private const string AllZeroTrace = "00000000000000000000000000000000";
    private const string AllZeroSpan = "0000000000000000";

    public static (string? TraceId, string? SpanId, string? ParentSpanId) Current()
    {
        var activity = Activity.Current;
        if (activity is null) return (null, null, null);

        var traceId = activity.TraceId.ToHexString();
        if (string.IsNullOrEmpty(traceId) || traceId == AllZeroTrace) return (null, null, null);

        var spanId = Span(activity.SpanId.ToHexString());
        // A parent without its own span has nothing to hang from, the same rule a span follows without its trace.
        var parentSpanId = spanId is null ? null : Span(activity.ParentSpanId.ToHexString());
        return (traceId, spanId, parentSpanId);
    }

    private static string? Span(string? value) =>
        string.IsNullOrEmpty(value) || value == AllZeroSpan ? null : value;
}
