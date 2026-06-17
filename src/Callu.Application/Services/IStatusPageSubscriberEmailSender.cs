namespace Callu.Application.Services;

/// <summary>
/// Sends the subscriber email for a status-page incident. Shared by the worker consumer
/// (broker mode) and the in-process notifier (no-broker mode).
/// </summary>
public interface IStatusPageSubscriberEmailSender
{
    /// <summary>
    /// Emails every confirmed subscriber of the incident's status page with its current
    /// title and status. No-ops if the incident or page is gone, subscriptions are off,
    /// or there are no confirmed subscribers.
    /// </summary>
    Task SendForIncidentAsync(Guid statusPageIncidentId, CancellationToken cancellationToken = default);
}
