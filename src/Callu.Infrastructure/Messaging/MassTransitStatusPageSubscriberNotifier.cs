using Callu.Application.Contracts.Messages;
using Callu.Application.Messaging;
using MassTransit;

namespace Callu.Infrastructure.Messaging;

/// <summary>
/// Broker-mode notifier. Publishes the message for the worker to send the subscriber emails,
/// keeping delivery off the API request path and behind the worker's retry pipeline.
/// </summary>
public sealed class MassTransitStatusPageSubscriberNotifier(IPublishEndpoint publishEndpoint)
    : IStatusPageSubscriberNotifier
{
    public Task NotifyAsync(Guid statusPageIncidentId, CancellationToken cancellationToken = default) =>
        publishEndpoint.Publish(new NotifyStatusPageSubscribers(statusPageIncidentId), cancellationToken);
}
