namespace Callu.Application.Events;

/// <summary>
/// Dispatches communication events to the active provider's lifecycle handler.
/// If no active provider exists, events are silently dropped.
/// </summary>
public interface ICommunicationEventDispatcher
{
    /// <summary>
    /// Dispatch an event to the active provider's lifecycle handler
    /// </summary>
    Task DispatchAsync(ICommunicationEvent @event, CancellationToken cancellationToken = default);
}
