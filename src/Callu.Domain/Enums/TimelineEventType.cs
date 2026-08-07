namespace Callu.Domain.Enums;

/// <summary>
/// Types of timeline events for incidents
/// </summary>
public enum TimelineEventType
{
    Created = 1,
    Acknowledged = 2,
    Escalated = 3,
    Reassigned = 4,
    NoteAdded = 5,
    SeverityChanged = 6,
    StatusChanged = 7,
    Resolved = 8,
    Closed = 9,
    Reopened = 10,
    CallInitiated = 11,
    CallConnected = 12,
    CallFailed = 13,
    CallAcknowledged = 14,
    CallEscalated = 15,
    ConferenceCreated = 16
}
