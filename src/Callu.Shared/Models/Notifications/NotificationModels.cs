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
    public DateTime? LastNotifiedAt { get; set; }
    public int NotificationCount { get; set; }
    public DateTime CreatedAt { get; set; }
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
}

public class TestNotificationRequest
{
    public string Message { get; set; } = "Test notification from CalluApp";
}
