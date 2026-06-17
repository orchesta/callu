namespace Callu.Domain.Enums;

/// <summary>
/// Lifecycle of an outbound notification-channel (Slack/Teams/Webhook/email) delivery
/// attempt. Persisted as its string name via a value converter — see
/// <see cref="WebhookDeliveryStatus"/> for the same pattern.
/// </summary>
public enum NotificationChannelDeliveryStatus
{
    Succeeded,
    Failed,
    Retrying,
}
