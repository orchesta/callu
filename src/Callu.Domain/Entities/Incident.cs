using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents an incident in the system
/// </summary>
public class Incident : BaseEntity
{

    /// <summary>
    /// Incident title
    /// </summary>
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of the incident
    /// </summary>
    [StringLength(4000)]
    public string? Description { get; set; }

    /// <summary>
    /// Language code for TTS pronunciation of this incident's data (e.g. en-US)
    /// </summary>
    [StringLength(10)]
    public string DataLanguage { get; set; } = "en-US";

    /// <summary>
    /// Incident severity level
    /// </summary>
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Medium;

    /// <summary>
    /// Current status of the incident
    /// </summary>
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    /// <summary>
    /// When the incident started
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the incident was acknowledged
    /// </summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>
    /// When the incident was resolved
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// User ID who acknowledged the incident
    /// </summary>
    [StringLength(128)]
    public string? AcknowledgedBy { get; set; }

    /// <summary>
    /// User ID who resolved the incident
    /// </summary>
    [StringLength(128)]
    public string? ResolvedBy { get; set; }

    /// <summary>
    /// Associated service ID
    /// </summary>
    public Guid? ServiceId { get; set; }

    /// <summary>
    /// Navigation property for service
    /// </summary>
    public virtual Service? Service { get; set; }

    /// <summary>
    /// Assigned team ID
    /// </summary>
    public Guid? TeamId { get; set; }

    /// <summary>
    /// Navigation property for team
    /// </summary>
    public virtual Team? Team { get; set; }

    /// <summary>
    /// Notes/comments on this incident
    /// </summary>
    public virtual ICollection<IncidentNote> Notes { get; set; } = new List<IncidentNote>();

    /// <summary>
    /// Timeline events for this incident
    /// </summary>
    public virtual ICollection<IncidentTimelineEvent> TimelineEvents { get; set; } = new List<IncidentTimelineEvent>();

    /// <summary>
    /// Notifications related to this incident
    /// </summary>
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    /// <summary>
    /// Call logs for this incident (VoxEngine call attempts)
    /// </summary>
    public virtual ICollection<CallLog> CallLogs { get; set; } = new List<CallLog>();

    /// <summary>
    /// Escalation policy being used for this incident
    /// </summary>
    public Guid? EscalationPolicyId { get; set; }

    /// <summary>
    /// Navigation property for escalation policy
    /// </summary>
    public virtual EscalationPolicy? EscalationPolicy { get; set; }

    /// <summary>
    /// Current escalation step ID
    /// </summary>
    public Guid? CurrentEscalationStepId { get; set; }

    /// <summary>
    /// Navigation property for current escalation step
    /// </summary>
    public virtual EscalationStep? CurrentEscalationStep { get; set; }

    /// <summary>
    /// When escalation was started
    /// </summary>
    public DateTime? EscalationStartedAt { get; set; }

    /// <summary>
    /// When the current step was triggered
    /// </summary>
    public DateTime? LastEscalationStepAt { get; set; }

    /// <summary>
    /// Is escalation currently active
    /// </summary>
    public bool IsEscalationActive { get; set; } = false;

    /// <summary>
    /// When set, the org-level channel notification dispatch (Slack/Teams/webhook/email)
    /// is skipped for this incident. Flipped by AlertRule actions with type
    /// <c>SuppressNotification</c>. Fix 10.P0-3.
    /// </summary>
    public bool IsNotificationSuppressed { get; set; } = false;

    /// <summary>
    /// Integration that originated this incident (for outbound feedback)
    /// </summary>
    public Guid? SourceIntegrationId { get; set; }

    /// <summary>
    /// Navigation property for source integration
    /// </summary>
    public virtual Integration? SourceIntegration { get; set; }

    /// <summary>
    /// External alert ID from the source system (for correlation/deduplication)
    /// </summary>
    [StringLength(200)]
    public string? ExternalAlertId { get; set; }

    /// <summary>
    /// Acknowledge this incident. Only Open incidents can be acknowledged.
    /// </summary>
    /// <exception cref="InvalidOperationException">When incident is not in Open status</exception>
    public void Acknowledge(string userId)
    {
        if (Status != IncidentStatus.Open)
            throw new InvalidOperationException($"Cannot acknowledge incident in '{Status}' status. Only 'Open' incidents can be acknowledged.");

        Status = IncidentStatus.Acknowledged;
        AcknowledgedAt = DateTime.UtcNow;
        AcknowledgedBy = userId;
        UpdatedAt = DateTime.UtcNow;
        IsEscalationActive = false;
    }

    /// <summary>
    /// Resolve this incident. Already resolved incidents cannot be resolved again.
    /// </summary>
    /// <exception cref="InvalidOperationException">When incident is already Resolved</exception>
    public void Resolve(string userId)
    {
        if (Status == IncidentStatus.Resolved || Status == IncidentStatus.Closed)
            throw new InvalidOperationException($"Cannot resolve incident in '{Status}' status.");

        Status = IncidentStatus.Resolved;
        ResolvedAt = DateTime.UtcNow;
        ResolvedBy = userId;
        UpdatedAt = DateTime.UtcNow;
        IsEscalationActive = false;
    }

    /// <summary>
    /// Move this incident into Investigating. Allowed from Open or Acknowledged.
    /// </summary>
    public void StartInvestigation(string userId)
    {
        if (Status != IncidentStatus.Open && Status != IncidentStatus.Acknowledged)
            throw new InvalidOperationException($"Cannot start investigation from '{Status}'.");

        if (Status == IncidentStatus.Open)
        {
            AcknowledgedAt = DateTime.UtcNow;
            AcknowledgedBy = userId;
            IsEscalationActive = false;
        }

        Status = IncidentStatus.Investigating;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark this incident as mitigated. Allowed from Acknowledged or Investigating.
    /// </summary>
    public void Mitigate(string userId)
    {
        if (Status != IncidentStatus.Acknowledged && Status != IncidentStatus.Investigating)
            throw new InvalidOperationException($"Cannot mitigate incident in '{Status}' status.");

        Status = IncidentStatus.Mitigated;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Close a resolved incident. Closed incidents are terminal and no longer considered active.
    /// </summary>
    public void Close(string userId)
    {
        if (Status != IncidentStatus.Resolved)
            throw new InvalidOperationException($"Only resolved incidents can be closed. Current: '{Status}'.");

        Status = IncidentStatus.Closed;
        UpdatedAt = DateTime.UtcNow;
        IsEscalationActive = false;
    }

    /// <summary>
    /// Reopen a resolved or closed incident back to Open.
    /// </summary>
    public void Reopen(string userId)
    {
        if (Status != IncidentStatus.Resolved && Status != IncidentStatus.Closed)
            throw new InvalidOperationException($"Only resolved/closed incidents can be reopened. Current: '{Status}'.");

        Status = IncidentStatus.Open;
        ResolvedAt = null;
        ResolvedBy = null;
        AcknowledgedAt = null;
        AcknowledgedBy = null;
        UpdatedAt = DateTime.UtcNow;

        EscalationPolicyId = null;
        CurrentEscalationStepId = null;
        EscalationStartedAt = null;
        LastEscalationStepAt = null;
        IsEscalationActive = false;
    }

    /// <summary>
    /// Apply a target status via the appropriate state-transition method.
    /// Centralizes the state machine so callers cannot bypass guard rails by assigning Status directly.
    /// </summary>
    public void ChangeStatus(IncidentStatus target, string userId)
    {
        if (Status == target) return;

        switch (target)
        {
            case IncidentStatus.Acknowledged:
                Acknowledge(userId);
                break;
            case IncidentStatus.Investigating:
                StartInvestigation(userId);
                break;
            case IncidentStatus.Mitigated:
                Mitigate(userId);
                break;
            case IncidentStatus.Resolved:
                Resolve(userId);
                break;
            case IncidentStatus.Closed:
                Close(userId);
                break;
            case IncidentStatus.Open:
                Reopen(userId);
                break;
            default:
                throw new InvalidOperationException($"Unsupported target status '{target}'.");
        }
    }

    /// <summary>
    /// True when the incident is in a terminal state (Resolved or Closed).
    /// Prefer this over numeric comparison on <see cref="IncidentStatus"/>;
    /// the enum's numeric ordering does not match the lifecycle order.
    /// </summary>
    public bool IsTerminal() => Status.IsTerminal();

    /// <summary>
    /// True when the incident is still active (not Resolved and not Closed).
    /// </summary>
    public bool IsActive() => Status.IsActive();

    /// <summary>
    /// Soft-delete this incident.
    /// </summary>
    public void MarkDeleted()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}

