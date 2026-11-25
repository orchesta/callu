using System.Diagnostics;
using Callu.Infrastructure.Audit;

namespace Callu.Tests;

/// <summary>The identifiers that let one operation be followed across services and messages.</summary>
// These are observational metadata, not proof of anything: the point is only that an audit row can be
// joined to the application logs and traces of the same execution.
public class OpenAuditCorrelationTests
{
    // ---------------------------------------------------------------- the ambient context

    /// <summary>The identifiers come from the active trace, never minted for the audit row.</summary>
    [Fact]
    public void TheTraceContextIsReadFromTheActiveActivity()
    {
        using var source = new ActivitySource(nameof(TheTraceContextIsReadFromTheActiveActivity));
        using var listener = Listen(source.Name);
        using var activity = source.StartActivity("audited-operation");

        Assert.NotNull(activity);
        var (traceId, spanId, _) = AuditTraceContext.Current();

        Assert.Equal(activity.TraceId.ToHexString(), traceId);
        Assert.Equal(activity.SpanId.ToHexString(), spanId);
    }

    /// <summary>An audit row is written whether or not the trace is sampled; the ids come along regardless.</summary>
    // Tracing is a sampled diagnostic and auditing is a complete record. A trail that appears only for
    // sampled requests is a sampled trail, and the gap is invisible until the missing row is the one
    // that matters.
    [Fact]
    public void AnUnsampledTraceStillReportsItsIdentifiers()
    {
        using var source = new ActivitySource(nameof(AnUnsampledTraceStillReportsItsIdentifiers));
        using var listener = Listen(source.Name, ActivitySamplingResult.PropagationData);
        using var activity = source.StartActivity("audited-operation");

        Assert.NotNull(activity);
        Assert.False(activity.IsAllDataRequested);

        var (traceId, _, _) = AuditTraceContext.Current();
        Assert.Equal(activity.TraceId.ToHexString(), traceId);
    }

    [Fact]
    public void NoActiveTraceIsNotAnError()
    {
        Assert.Equal((null, null, null), AuditTraceContext.Current());
    }

    private static ActivityListener Listen(
        string sourceName, ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
