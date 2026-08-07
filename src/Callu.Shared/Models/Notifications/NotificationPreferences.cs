namespace Callu.Shared.Models.Notifications;

/// <summary>
/// Notification preferences
/// </summary>
public record NotificationPreferences
{
    public bool EmailNotifications { get; init; } = true;
    public bool SmsNotifications { get; init; } = false;
    public bool VoiceNotifications { get; init; } = false;
    public bool PushNotifications { get; init; } = true;
    public string? QuietHoursStart { get; init; }
    public string? QuietHoursEnd { get; init; }
    public string Timezone { get; init; } = "UTC";
}
