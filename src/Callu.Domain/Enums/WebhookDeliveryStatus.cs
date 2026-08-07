namespace Callu.Domain.Enums;

/// <summary>Lifecycle of an outbound webhook (incident-ACK) delivery attempt, persisted as its string name.</summary>
public enum WebhookDeliveryStatus
{
    Pending,
    Succeeded,
    Failed,
    Retrying,
}
