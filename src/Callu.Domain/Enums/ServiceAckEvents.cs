namespace Callu.Domain.Enums;

/// <summary>
/// Incident lifecycle events a service's ACK callback can subscribe to.
/// </summary>
[Flags]
public enum ServiceAckEvents
{
    None = 0,
    Created = 1,
    Acknowledged = 2,
    Resolved = 4,
    Closed = 8,
    Reopened = 16,
}
