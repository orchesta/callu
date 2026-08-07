namespace Callu.Application.Messaging;

/// <summary>
/// Triggers the subscriber notification for a status-page incident. Broker-mode publishes a
/// message for the worker to send; no-broker mode sends in-process.
/// </summary>
public interface IStatusPageSubscriberNotifier
{
    Task NotifyAsync(Guid statusPageIncidentId, CancellationToken cancellationToken = default);
}
