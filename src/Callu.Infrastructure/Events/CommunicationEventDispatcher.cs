using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Events;
using Callu.Application.Providers;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Events;

/// <summary>
/// Routes communication events to the active provider's lifecycle handler.
/// Finds the active (enabled) provider from DB, resolves its ICommunicationProviderLifecycle,
/// and dispatches the event. If no active provider or no matching handler, events are silently dropped.
/// </summary>
public class CommunicationEventDispatcher(
    ICommunicationProviderRepository communicationProviders,
    IEnumerable<ICommunicationProviderLifecycle> lifecycles,
    ILogger<CommunicationEventDispatcher> logger) : ICommunicationEventDispatcher
{
    public async Task DispatchAsync(ICommunicationEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            var activeProvider = await communicationProviders.GetHighestPriorityEnabledNoTrackingAsync(cancellationToken);

            if (activeProvider == null)
            {
                logger.LogDebug("No active communication provider, dropping event {EventType}", @event.GetType().Name);
                return;
            }

            var lifecycle = lifecycles.FirstOrDefault(l =>
                l.ProviderType.Equals(activeProvider.ProviderType, StringComparison.OrdinalIgnoreCase));

            if (lifecycle == null)
            {
                logger.LogDebug("No lifecycle handler for provider type {ProviderType}, dropping event {EventType}",
                    activeProvider.ProviderType, @event.GetType().Name);
                return;
            }

            switch (@event)
            {
                case TeamMemberAddedEvent added:
                    await lifecycle.OnTeamMemberAddedAsync(activeProvider.Id, added.UserId, added.DisplayName, cancellationToken);
                    break;

                case TeamMemberRemovedEvent removed:
                    await lifecycle.OnTeamMemberRemovedAsync(activeProvider.Id, removed.UserId, cancellationToken);
                    break;

                default:
                    logger.LogWarning("Unknown communication event type: {EventType}", @event.GetType().Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error dispatching communication event {EventType}", @event.GetType().Name);
        }
    }
}
