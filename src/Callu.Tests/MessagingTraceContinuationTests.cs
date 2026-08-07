using System.Diagnostics;
using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Messaging.Consuming;
using Callu.Infrastructure.Telemetry;

namespace Callu.Tests;

/// <summary>The Worker's handling belongs to the trace the message was staged in, not to a trace of its own.</summary>
public class MessagingTraceContinuationTests : IDisposable
{
    private readonly ActivityListener _listener;

    public MessagingTraceContinuationTests()
    {
        // Without a listener that samples, StartActivity returns null and every assertion below would
        // be vacuously true.
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelemetryExtensions.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private static string TraceParent(string traceId = TraceId) => $"00-{traceId}-00f067aa0ba902b7-01";

    [Fact]
    public void AConsumedMessage_JoinsTheTraceItWasStagedIn()
    {
        using var activity = MessagingActivity.StartConsume(
            CalluTopology.TriggerIncidentEscalationMessage, TraceParent());

        Assert.NotNull(activity);
        Assert.Equal(TraceId, activity.TraceId.ToString());
        Assert.Equal("00f067aa0ba902b7", activity.ParentSpanId.ToString());
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
    }

    /// <summary>Two messages from the same request stay in one trace; two unrelated ones do not merge.</summary>
    [Fact]
    public void TwoMessagesFromDifferentRequests_StayInDifferentTraces()
    {
        const string other = "0af7651916cd43dd8448eb211c80319c";

        using var first = MessagingActivity.StartConsume("a", TraceParent());
        using var second = MessagingActivity.StartConsume("b", TraceParent(other));

        Assert.NotEqual(first!.TraceId.ToString(), second!.TraceId.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-traceparent")]
    [InlineData("00-tooshort-00f067aa0ba902b7-01")]
    public void AMissingOrUnusableHeader_StillStartsAnActivity(string? traceParent)
    {
        // A header nobody can parse must not cost a page: the delivery is handled either way, it just
        // starts its own trace.
        using var activity = MessagingActivity.StartConsume(
            CalluTopology.TriggerIncidentEscalationMessage, traceParent);

        Assert.NotNull(activity);
        Assert.Equal(default, activity.ParentSpanId);
    }

    /// <summary>The span is named after the wire message, so a trace says which message it was.</summary>
    [Fact]
    public void TheSpan_IsNamedAfterTheWireMessage()
    {
        using var activity = MessagingActivity.StartConsume(
            CalluTopology.NotifyStatusPageSubscribersMessage, TraceParent());

        Assert.Equal($"consume {CalluTopology.NotifyStatusPageSubscribersMessage}", activity!.DisplayName);
    }

    /// <summary>Both entry points resume the trace: the delivery, and the sweep that retries from the row.</summary>
    [Fact]
    public void BothTheConsumerHostAndTheSweep_ResumeTheTrace()
    {
        var host = SourceScanner.Code(SourceScanner.ProductFiles()
            .Single(f => Path.GetFileName(f) == "CalluConsumerHost.cs"));
        var sweep = SourceScanner.Code(SourceScanner.ProductFiles()
            .Single(f => Path.GetFileName(f) == "InboxRetrySweep.cs"));

        Assert.Contains("MessagingActivity.StartConsume", host, StringComparison.Ordinal);
        Assert.Contains("MessagingActivity.StartConsume", sweep, StringComparison.Ordinal);
    }
}
