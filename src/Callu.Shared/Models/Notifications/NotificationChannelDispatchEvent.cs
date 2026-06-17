namespace Callu.Shared.Models.Notifications;

/// <summary>
/// Which incident lifecycle step triggers org-level notification channels (Slack, Teams, webhook, ops email).
/// </summary>
public enum NotificationChannelDispatchEvent
{
    IncidentCreated = 0,
    IncidentAcknowledged = 1,
    IncidentResolved = 2,
    IncidentClosed = 3,
    IncidentReopened = 4,
}
