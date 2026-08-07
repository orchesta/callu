using Callu.Application.Messaging;
using Callu.Application.Services;

namespace Callu.Infrastructure.Messaging;

/// <summary>
/// No-broker notifier. Sends the subscriber email in-process; the manual trigger awaits it
/// within the request, so the request scope (and its DbContext) is still alive.
/// </summary>
public sealed class DirectStatusPageSubscriberNotifier(IStatusPageSubscriberEmailSender sender)
    : IStatusPageSubscriberNotifier
{
    public Task NotifyAsync(Guid statusPageIncidentId, CancellationToken cancellationToken = default) =>
        sender.SendForIncidentAsync(statusPageIncidentId, cancellationToken);
}
