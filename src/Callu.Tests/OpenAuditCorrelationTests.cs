using System.Diagnostics;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Shared.Models.Audit;

namespace Callu.Tests;

/// <summary>The identifiers that let one operation be followed across services and messages.</summary>
// These are observational metadata, not proof of anything: the point is only that an audit row can be
// joined to the application logs and traces of the same execution.
public class OpenAuditCorrelationTests
{
    private static AuditLogDto Entry(
        string? traceId = null, string? spanId = null, string? correlationId = null, string? requestId = null) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
        EventName = "incident.case.create",
        EventCategory = "incident-management",
        Outcome = AuditOutcome.Success,
        ResourceType = "Incident",
        ActorId = "user-1",
        TraceId = traceId,
        SpanId = spanId,
        CorrelationId = correlationId,
        RequestId = requestId,
    };

    private static OpenAuditRequestContext? Request(AuditLogDto entry) =>
        OpenAuditEventMapper.ToEnvelope(entry, "callu", "production").Request;

    [Fact]
    public void TheTraceAndSpanReachTheExportedEvent()
    {
        var request = Request(Entry(
            traceId: "4bf92f3577b34da6a3ce929d0e0e4736", spanId: "00f067aa0ba902b7"));

        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", request?.TraceId);
        Assert.Equal("00f067aa0ba902b7", request?.SpanId);
    }

    /// <summary>The logical operation survives when the trace does not, so it stands on its own.</summary>
    [Fact]
    public void ACorrelationIdIsCarriedWithNoTraceAtAll()
    {
        var request = Request(Entry(correlationId: "incident-3738f0c6"));

        Assert.Equal("incident-3738f0c6", request?.CorrelationId);
        Assert.Null(request?.TraceId);
    }

    // A span identifier alone cannot be resolved: there is nothing to look it up in.
    [Fact]
    public void ASpanIsNotExportedWithoutItsTrace()
    {
        var request = Request(Entry(spanId: "00f067aa0ba902b7"));

        Assert.Null(request?.SpanId);
    }

    /// <summary>The all-zero context is what a dead Activity reports; it correlates with nothing.</summary>
    [Theory]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("4BF92F3577B34DA6A3CE929D0E0E4736")]
    [InlineData("4bf92f3577b34da6")]
    [InlineData("not-hex-at-all-not-hex-at-allxx!")]
    public void ATraceIdTheSchemaWouldRejectIsNotExported(string traceId)
    {
        Assert.Null(Request(Entry(traceId: traceId))?.TraceId);
    }

    /// <summary>An optional object must carry something; an entry with no context omits it entirely.</summary>
    [Fact]
    public void AnEntryWithNoRequestContextExportsNoRequestObject()
    {
        Assert.Null(Request(Entry()));
    }

    // ---------------------------------------------------------------- the ambient context

    /// <summary>The identifiers come from the active trace, never minted for the audit row.</summary>
    [Fact]
    public void TheTraceContextIsReadFromTheActiveActivity()
    {
        using var source = new ActivitySource(nameof(TheTraceContextIsReadFromTheActiveActivity));
        using var listener = Listen(source.Name);
        using var activity = source.StartActivity("audited-operation");

        Assert.NotNull(activity);
        var (traceId, spanId) = AuditTraceContext.Current();

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

        var (traceId, _) = AuditTraceContext.Current();
        Assert.Equal(activity.TraceId.ToHexString(), traceId);
    }

    [Fact]
    public void NoActiveTraceIsNotAnError()
    {
        Assert.Equal((null, null), AuditTraceContext.Current());
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
