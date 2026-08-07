using Callu.Application.Contracts.Messages;
using Callu.Application.Messaging;

namespace Callu.Infrastructure.Messaging.Outbox;

/// <summary>Broker-mode notifier that stages the message so subscriber email stays off the request path.</summary>
public sealed class OutboxStatusPageSubscriberNotifier(IOutboxWriter outbox) : IStatusPageSubscriberNotifier
{
    public Task NotifyAsync(Guid statusPageIncidentId, CancellationToken cancellationToken = default)
    {
        // No nudge: this interface has no post-commit phase, and a nudge before the commit would wake the
        // dispatcher onto a row it cannot see yet. Subscriber email waits for the next poll.
        outbox.Stage(
            CalluTopology.NotifyStatusPageSubscribersMessage,
            new NotifyStatusPageSubscribers(statusPageIncidentId));

        return Task.CompletedTask;
    }
}
