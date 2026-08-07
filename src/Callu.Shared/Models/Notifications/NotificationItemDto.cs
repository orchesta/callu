namespace Callu.Shared.Models.Notifications;

/// <summary>
/// Notification item for UI display (header dropdown, notifications page)
/// </summary>
public record NotificationItemDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string? Message { get; init; }
    
    /// <summary>
    /// Display type: "incident", "escalation", "resolved", "info"
    /// </summary>
    public string Type { get; init; } = "info";
    
    public string? ActionUrl { get; init; }
    public bool IsRead { get; init; }
    public DateTime CreatedAt { get; init; }
    
    /// <summary>
    /// Human-readable relative time (e.g., "2 min ago", "1 hour ago")
    /// </summary>
    public string TimeAgo { get; init; } = "";
}
