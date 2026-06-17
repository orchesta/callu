namespace Callu.Application.Events;

/// <summary>
/// Marker interface for communication-related domain events.
/// Events are dispatched to the active communication provider's lifecycle handler.
/// </summary>
public interface ICommunicationEvent { }
