using System.Text.RegularExpressions;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Audit;

/// <summary>Derives an OpenAuditModel event.name/category/outcome from a (resource, action) pair.</summary>
public static partial class AuditEventNaming
{
    public static string ToEventName(string resourceType, AuditAction action) =>
        $"{Slugify(resourceType)}.{ToActionSegment(action)}";

    /// <summary>The action segment of an event name.</summary>
    // An imperative verb, and the outcome kept out of it where another action already names the same
    // operation succeeding. Where the suffix is a reason rather than an outcome it stays: an auditor
    // filters for the times nobody was reached, and one shared name would take that away.
    public static string ToActionSegment(AuditAction action) => action switch
    {
        AuditAction.Created => "create",
        AuditAction.Updated => "update",
        AuditAction.Deleted => "delete",
        AuditAction.Viewed => "view",
        AuditAction.Login or AuditAction.LoginFailed => "login",
        AuditAction.Logout => "logout",
        AuditAction.PasswordChanged => "password-change",
        AuditAction.RoleAssigned => "role-assign",
        AuditAction.RoleRemoved => "role-remove",
        AuditAction.SettingsChanged => "settings-change",
        AuditAction.Escalated => "escalate",
        AuditAction.Reassigned => "reassign",
        AuditAction.Acknowledged => "acknowledge",
        AuditAction.Resolved => "resolve",
        AuditAction.Closed => "close",
        AuditAction.Reopened => "reopen",
        AuditAction.Exported => "export",
        AuditAction.OverrideCreated => "override-create",
        AuditAction.OverrideCancelled => "override-cancel",
        AuditAction.EscalationExhausted => "escalation-exhaust",
        AuditAction.EscalationNobodyReached => "escalation-nobody-reached",
        AuditAction.EscalationTargetsUnpageable => "escalation-targets-unpageable",
        AuditAction.EscalationChannelsSilent => "escalation-channels-silent",
        AuditAction.EscalationChannelsPartiallySilent => "escalation-channels-partially-silent",
        AuditAction.EscalationDispatchFailed => "escalation-dispatch-failed",
        AuditAction.EscalationDispatchFailing => "escalation-dispatch-failing",
        AuditAction.EscalationDispatchPartiallyFailed => "escalation-dispatch-partially-failed",
        AuditAction.EscalationTriggerFailed => "escalation-trigger-failed",
        AuditAction.DispatchFailed => "dispatch-failed",
        AuditAction.Suppressed => "suppress",
        AuditAction.MarkedUsed => "mark-used",
        AuditAction.Cascaded => "cascade",
        AuditAction.Submitted => "submit",
        AuditAction.Rejected => "reject",
        AuditAction.Published => "publish",
        AuditAction.Locked => "lock",
        AuditAction.IntegrityVerified or AuditAction.IntegrityBroken => "integrity-verify",
        AuditAction.VoiceCallLost => "voice-call-lost",
        AuditAction.VoiceCallNeverConfirmed => "voice-call-never-confirmed",
        AuditAction.ConferenceInviteReachedNobody => "conference-invite-reached-nobody",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "No event name for this action."),
    };

    public static string ToEventCategory(string resourceType) => resourceType switch
    {
        "Incident" or "Escalation" => "incident-management",
        "User" or "Team" => "identity-and-access-management",
        "AuditLog" => "audit-management",
        _ => "platform-administration",
    };

    public static AuditOutcome ToOutcome(AuditAction action) => action switch
    {
        AuditAction.LoginFailed
            or AuditAction.DispatchFailed
            or AuditAction.EscalationDispatchFailed
            or AuditAction.EscalationTriggerFailed
            or AuditAction.EscalationNobodyReached
            or AuditAction.EscalationTargetsUnpageable
            or AuditAction.EscalationChannelsSilent
            or AuditAction.IntegrityBroken
            or AuditAction.VoiceCallLost
            or AuditAction.VoiceCallNeverConfirmed
            or AuditAction.ConferenceInviteReachedNobody
            or AuditAction.Rejected => AuditOutcome.Failure,
        AuditAction.EscalationDispatchPartiallyFailed
            or AuditAction.EscalationChannelsPartiallySilent
            or AuditAction.EscalationDispatchFailing => AuditOutcome.Partial,
        _ => AuditOutcome.Success,
    };

    private static string Slugify(string pascalCase) =>
        PascalBoundary().Replace(pascalCase, "-").TrimStart('-').ToLowerInvariant();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex PascalBoundary();
}
