using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Callu.Infrastructure.Telemetry;

/// <summary>
/// Adds TraceId and SpanId from <see cref="Activity.Current"/> so logs align with OpenTelemetry traces.
/// </summary>
public sealed class ActivityTraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
