namespace Callu.Domain.Enums;

/// <summary>Lifecycle of an outbound notification-channel delivery attempt, persisted as its string name.</summary>
public enum NotificationChannelDeliveryStatus
{
    Succeeded,
    Failed,
    Retrying,
}
