using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// A notification channel configuration (Slack, Teams, Email, custom webhook).
/// Used to dispatch alert notifications to external systems.
/// </summary>
public class NotificationChannel : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Channel type: Slack, MicrosoftTeams, Email, Webhook
    /// </summary>
    public NotificationChannelType ChannelType { get; set; }

    /// <summary>
    /// JSON configuration specific to the channel type
    /// E.g. {"webhookUrl": "..."} for Slack, {"email": "..."} for Email
    /// </summary>
    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>
    /// Whether this channel is active and should receive notifications
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional: only send notifications for incidents with this severity or higher
    /// </summary>
    [MaxLength(20)]
    public string? MinimumSeverity { get; set; }

    /// <summary>
    /// Optional: only notify for specific services (JSON array of service IDs, empty = all)
    /// </summary>
    public string ServiceFilterJson { get; set; } = "[]";

    /// <summary>
    /// Last time a notification was sent through this channel
    /// </summary>
    public DateTime? LastNotifiedAt { get; set; }

    /// <summary>
    /// Total notifications sent
    /// </summary>
    public int NotificationCount { get; set; }

    /// <summary>Fire when a new incident is created.</summary>
    public bool NotifyOnIncidentCreated { get; set; } = true;

    /// <summary>Fire when an incident is acknowledged.</summary>
    public bool NotifyOnIncidentAcknowledged { get; set; }

    /// <summary>Fire when an incident is resolved.</summary>
    public bool NotifyOnIncidentResolved { get; set; }
}

public enum NotificationChannelType
{
    Slack,
    MicrosoftTeams,
    Email,
    Webhook
}
