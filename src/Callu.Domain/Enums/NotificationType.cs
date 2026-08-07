namespace Callu.Domain.Enums;

/// <summary>
/// Types of notification channels
/// </summary>
public enum NotificationType
{
    Email = 1,
    Sms = 2,
    Push = 3,
    Slack = 4,
    MsTeams = 5,
    Webhook = 6,
    VoiceCall = 7
}
