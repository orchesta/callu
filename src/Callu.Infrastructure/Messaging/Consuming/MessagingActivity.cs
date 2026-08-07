using System.Diagnostics;
using Callu.Infrastructure.Telemetry;

namespace Callu.Infrastructure.Messaging.Consuming;

/// <summary>Resumes the trace the message was staged in, so the API's request and the Worker's handling are one trace.</summary>
internal static class MessagingActivity
{
    public static Activity? StartConsume(string messageType, string? traceParent)
    {
        var name = $"consume {messageType}";

        // A header that cannot be parsed must not cost a page: the delivery is still handled, it just
        // starts its own trace.
        return ActivityContext.TryParse(traceParent, traceState: null, out var parent)
            ? TelemetryExtensions.ActivitySource.StartActivity(name, ActivityKind.Consumer, parent)
            : TelemetryExtensions.ActivitySource.StartActivity(name, ActivityKind.Consumer);
    }
}
