namespace Callu.Domain.Enums;

/// <summary>
/// Types of audit actions
/// </summary>
public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    Viewed = 4,
    Login = 5,
    Logout = 6,
    PasswordChanged = 7,
    RoleAssigned = 8,
    RoleRemoved = 9,
    SettingsChanged = 10,

    // Appended only. The stored value is an int, so renumbering any member above would silently
    // relabel every existing row.
    Escalated = 11,
    Reassigned = 12,
    Acknowledged = 13,
    Resolved = 14,
    Closed = 15,
    Reopened = 16,
    LoginFailed = 17,
    Exported = 18,
    OverrideCreated = 19,
    OverrideCancelled = 20,

    // Escalation outcomes. These were all stored as Updated with the name in Description, so an
    // auditor could not filter for "the times nobody was reached".
    EscalationExhausted = 21,
    EscalationNobodyReached = 22,
    EscalationTargetsUnpageable = 23,
    EscalationChannelsSilent = 24,
    EscalationChannelsPartiallySilent = 25,
    EscalationDispatchFailed = 26,
    EscalationDispatchFailing = 27,
    EscalationDispatchPartiallyFailed = 28,
    EscalationTriggerFailed = 29,
    DispatchFailed = 30,
    Suppressed = 31,
    MarkedUsed = 32,
    Cascaded = 33,

    // Postmortem lifecycle.
    Submitted = 34,
    Rejected = 35,
    Published = 36,
    Locked = 37,

    // Tamper-evidence.
    IntegrityVerified = 38,
    IntegrityBroken = 39,

    // Voice call reporting.
    VoiceCallLost = 40,
    ConferenceInviteReachedNobody = 41,

    /// <summary>A page the provider accepted, for which no call was ever recorded.</summary>
    VoiceCallNeverConfirmed = 42
}
