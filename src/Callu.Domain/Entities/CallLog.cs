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

    /// <summary>
    /// Failure reason (SIP error code/reason, network error, etc.)
    /// </summary>
    [StringLength(500)]
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

    /// <summary>
    /// UTC time at which a retry call should be attempted. Set when the call ends in a
    /// non-terminal-success status (Failed / NoAnswer / Voicemail / Timeout) and cleared
    /// once the retry has been fired. Null means "no pending retry". The
    /// VoiceCallRetryQuartzJob scans for rows where this is due.
    /// Replaces the previous Task.Delay fire-and-forget retry which lost pending retries
    /// across process restarts and targeted the wrong user (incident.CreatedBy).
    /// </summary>
    public DateTime? NextRetryAt { get; set; }
}
