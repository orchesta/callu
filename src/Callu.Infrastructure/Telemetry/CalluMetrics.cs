using System.Diagnostics.Metrics;

namespace Callu.Infrastructure.Telemetry;

/// <summary>
/// Application-specific counters and histograms (OTLP / Prometheus-compatible names).
/// </summary>
public sealed class CalluMetrics
{
    public const string MeterName = "Callu.App";

    private readonly Counter<long> _incidentsCreated;
    private readonly Counter<long> _notificationsSent;
    private readonly Counter<long> _notificationsFailed;
    private readonly Counter<long> _escalationStepsTriggered;
    private readonly Histogram<double> _notificationLatency;
    private readonly Histogram<double> _webhookProcessingDuration;
    private readonly Histogram<double> _escalationProcessingDuration;

    public CalluMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _incidentsCreated = meter.CreateCounter<long>(
            "callu.incidents.created",
            "incidents",
            "Total incidents created");

        _notificationsSent = meter.CreateCounter<long>(
            "callu.notifications.sent",
            "notifications",
            "Total notifications sent successfully");

        _notificationsFailed = meter.CreateCounter<long>(
            "callu.notifications.failed",
            "notifications",
            "Total notifications that failed delivery");

        _escalationStepsTriggered = meter.CreateCounter<long>(
            "callu.escalations.steps_triggered",
            "steps",
            "Total escalation steps triggered");

        _notificationLatency = meter.CreateHistogram<double>(
            "callu.notifications.latency",
            "ms",
            "Time from notification creation to delivery");

        _webhookProcessingDuration = meter.CreateHistogram<double>(
            "callu.webhooks.processing_duration",
            "ms",
            "Webhook processing duration");

        _escalationProcessingDuration = meter.CreateHistogram<double>(
            "callu.escalations.processing_duration",
            "ms",
            "Escalation processing duration");
    }

    public void IncidentCreated(string severity)
        => _incidentsCreated.Add(1,
            new KeyValuePair<string, object?>("severity", severity));

    public void NotificationSent(string channel)
        => _notificationsSent.Add(1,
            new KeyValuePair<string, object?>("channel", channel));

    /// <param name="reason">
    /// MUST be a low-cardinality bounded value (e.g. permanent/auth/rate-limited/timeout/config/transient/unknown).
    /// Never pass raw provider error text — it explodes metric cardinality and can leak PII.
    /// See <see cref="NotificationDispatchMetrics.ClassifyFailure"/>.
    /// </param>
    public void NotificationFailed(string channel, string reason)
        => _notificationsFailed.Add(1,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("reason", reason));

    public void EscalationStepTriggered(int level)
        => _escalationStepsTriggered.Add(1,
            new KeyValuePair<string, object?>("level", level));

    public void RecordNotificationLatency(double milliseconds, string channel)
        => _notificationLatency.Record(milliseconds,
            new KeyValuePair<string, object?>("channel", channel));

    public void RecordWebhookDuration(double milliseconds)
        => _webhookProcessingDuration.Record(milliseconds);

    public void RecordEscalationDuration(double milliseconds)
        => _escalationProcessingDuration.Record(milliseconds);
}
