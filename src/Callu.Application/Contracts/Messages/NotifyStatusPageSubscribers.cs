namespace Callu.Application.Contracts.Messages;

/// <summary>Published when an operator manually triggers a subscriber notification for a status-page
/// incident; consumed by the worker to send the emails.</summary>
public record NotifyStatusPageSubscribers(Guid StatusPageIncidentId);
