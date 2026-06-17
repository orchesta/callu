namespace Callu.Domain.Enums;

/// <summary>
/// Lifecycle of an outbound webhook (incident-ACK) delivery attempt. Persisted as its
/// string name via a value converter so the DB column stays human-readable varchar and
/// the retry job's partial index ("Status" = 'Retrying') keeps working unchanged.
/// </summary>
public enum WebhookDeliveryStatus
{
    Pending,
    Succeeded,
    Failed,
    Retrying,
}
