namespace Callu.Application.Contracts.Messages;

/// <summary>
/// Published when an operator manually triggers a subscriber notification for a status-page
/// incident, and consumed by the worker to send the emails.
///
/// Architectural consistency: every other outbound channel (escalation email / SMS / voice)
/// is dispatched from the worker with retries, so subscriber emails are too — keeping all
/// outbound delivery off the API request path and behind a single retrying pipeline. When no
/// broker is configured the API falls back to sending in-process.
/// </summary>
public record NotifyStatusPageSubscribers(Guid StatusPageIncidentId);
