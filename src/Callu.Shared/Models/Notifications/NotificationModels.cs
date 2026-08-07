namespace Callu.Shared.Models.Notifications;

public class NotificationChannelDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ChannelType { get; set; } = "Slack";
    public Dictionary<string, string> Configuration { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public string? MinimumSeverity { get; set; }
    public List<Guid> ServiceFilter { get; set; } = [];
    /// <summary>When true, send when a new incident is created.</summary>
    public bool NotifyOnIncidentCreated { get; set; } = true;
    /// <summary>When true, send when an incident is acknowledged.</summary>
    public bool NotifyOnIncidentAcknowledged { get; set; }
    /// <summary>When true, send when an incident is resolved.</summary>
    public bool NotifyOnIncidentResolved { get; set; }
    /// <summary>When true, send when an incident is closed.</summary>
    public bool NotifyOnIncidentClosed { get; set; }
    /// <summary>When true, send when an incident is reopened.</summary>
    public bool NotifyOnIncidentReopened { get; set; }
    public DateTime? LastNotifiedAt { get; set; }
    public int NotificationCount { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Outcome of the most recent delivery attempt, or null when nothing has been sent yet.</summary>
    public string? LastDeliveryStatus { get; set; }

    /// <summary>When that attempt was made. Unlike LastNotifiedAt, a failure updates this too.</summary>
    public DateTime? LastDeliveryAt { get; set; }
}

/// <summary>One outbound attempt on an org channel, as an operator reads it.</summary>
public class NotificationChannelDeliveryDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? HttpStatus { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }
    public DateTime AttemptedAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
}

public class CreateNotificationChannelRequest
{
    public string Name { get; set; } = string.Empty;
    public string ChannelType { get; set; } = "Slack";
    public Dictionary<string, string> Configuration { get; set; } = [];
    public string? MinimumSeverity { get; set; }
    public List<Guid> ServiceFilter { get; set; } = [];
    public bool NotifyOnIncidentCreated { get; set; } = true;
    public bool NotifyOnIncidentAcknowledged { get; set; }
    public bool NotifyOnIncidentResolved { get; set; }
    public bool NotifyOnIncidentClosed { get; set; }
    public bool NotifyOnIncidentReopened { get; set; }
}

public class UpdateNotificationChannelRequest
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Configuration { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public string? MinimumSeverity { get; set; }
    public List<Guid> ServiceFilter { get; set; } = [];
    public bool NotifyOnIncidentCreated { get; set; } = true;
    public bool NotifyOnIncidentAcknowledged { get; set; }
    public bool NotifyOnIncidentResolved { get; set; }
    public bool NotifyOnIncidentClosed { get; set; }
    public bool NotifyOnIncidentReopened { get; set; }
}

public class TestNotificationRequest
{
    public string Message { get; set; } = "Test notification from CalluApp";
}
