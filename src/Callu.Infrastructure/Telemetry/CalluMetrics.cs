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
    private readonly Counter<long> _escalationTriggersDeadLettered;
    private readonly Counter<long> _serviceActionExecutions;
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

        _escalationTriggersDeadLettered = meter.CreateCounter<long>(
            "callu.escalations.triggers_dead_lettered",
            "triggers",
            "Escalation triggers that dead-lettered after every retry, i.e. paging never started");

        _serviceActionExecutions = meter.CreateCounter<long>(
            "callu.service_actions.executions",
            "executions",
            "Outbound ACK callbacks and manual service actions by trigger kind and outcome");

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

    /// <summary>Counts a failed send; reason must be a bounded low-cardinality category from
    /// <see cref="NotificationDispatchMetrics.ClassifyFailure"/>, never raw provider error text.</summary>
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

    public void EscalationTriggerDeadLettered() => _escalationTriggersDeadLettered.Add(1);

    /// <summary>Counts one action execution; both tags are bounded vocabularies, never raw error text.</summary>
    public void ServiceActionExecution(string triggerKind, string outcome)
        => _serviceActionExecutions.Add(1,
            new KeyValuePair<string, object?>("trigger_kind", triggerKind),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordEscalationDuration(double milliseconds)
        => _escalationProcessingDuration.Record(milliseconds);
}
