using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Represents a single call attempt to a responder for an incident.
/// Tracks status, duration, retry attempts, and callback data from VoxEngine.
/// </summary>
public class CallLog : BaseEntity
{
    /// <summary>
    /// Incident that triggered this call
    /// </summary>
    public Guid IncidentId { get; set; }

    /// <summary>
    /// Navigation property for incident
    /// </summary>
    public virtual Incident Incident { get; set; } = null!;

    /// <summary>
    /// Target phone number (with country code)
    /// </summary>
    [Required]
    [StringLength(30)]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the person being called
    /// </summary>
    [StringLength(100)]
    public string? CalledPersonName { get; set; }

    /// <summary>
    /// Current status of this call
    /// </summary>
    public CallStatus Status { get; set; } = CallStatus.Initiated;

    /// <summary>
    /// Call duration in seconds
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Which attempt number this is (1 = first, 2 = first retry, etc.)
    /// </summary>
    public int AttemptNumber { get; set; } = 1;

    public const int MaxFailureReasonLength = 500;

    /// <summary>
    /// Failure reason (SIP error code/reason, network error, etc.)
    /// </summary>
    [StringLength(MaxFailureReasonLength)]
    public string? FailureReason { get; set; }

    /// <summary>
    /// When the call was initiated
    /// </summary>
    public DateTime InitiatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the call completed (answered, failed, or timed out)
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Extra metadata from VoxEngine callback (JSON)
    /// </summary>
    [StringLength(4000)]
    public string? MetadataJson { get; set; }

    /// <summary>
    /// VoxEngine call token used for this call (for correlation)
    /// </summary>
    [StringLength(100)]
    public string? CallToken { get; set; }

    /// <summary>The attempt this call was placed for: the notification row on a page, the call-log row on a retry.</summary>
    // Provider-neutral, unlike CallToken: every provider stores its own id shape there, so a guard
    // asking "did this page already dial" cannot read it. Null on rows written before this existed
    // and on providers that do not echo the attempt back.
    public Guid? AttemptId { get; set; }

    /// <summary>UTC time at which a retry call is due; null means no pending retry.</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>UTC time of the first dial-out failure in the current stuck period; null means not stuck.</summary>
    public DateTime? DialOutFailingSince { get; set; }

    /// <summary>Which failure governs the current stuck period — the most lenient kind seen in it.</summary>
    public VoiceDialOutFailureKind? DialOutFailureKind { get; set; }

    /// <summary>The single definition of standing this row's retry chain down; call history is untouched.</summary>
    public void StandDownRetryChain()
    {
        NextRetryAt = null;
        DialOutFailingSince = null;
        DialOutFailureKind = null;
    }
}
