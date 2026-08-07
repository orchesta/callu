namespace Callu.Application.Services;

/// <summary>
/// Sends the subscriber email for a status-page incident. Shared by the worker consumer
/// (broker mode) and the in-process notifier (no-broker mode).
/// </summary>
public interface IStatusPageSubscriberEmailSender
{
    /// <summary>Emails every confirmed subscriber of the incident's status page with its current title and
    /// status; no-ops when there is nothing or nobody to send to.</summary>
    Task SendForIncidentAsync(Guid statusPageIncidentId, CancellationToken cancellationToken = default);
}
