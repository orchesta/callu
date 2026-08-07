using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>One audit trail row, shaped after the OpenAuditModel event envelope.</summary>
public class AuditLog : BaseEntity
{
    /// <summary>OpenAuditModel actor.id — null when the actor is the system itself.</summary>
    [StringLength(128)]
    public string? ActorId { get; set; }

    /// <summary>OpenAuditModel actor.displayName.</summary>
    [StringLength(100)]
    public string? ActorDisplayName { get; set; }

    public AuditActorType ActorType { get; set; }

    /// <summary>The action taken. Appended-only — see the enum's own comment.</summary>
    public AuditAction Action { get; set; }

    /// <summary>OpenAuditModel event.name, derived from (ResourceType, Action).</summary>
    [Required]
    [StringLength(150)]
    public string EventName { get; set; } = string.Empty;

    /// <summary>OpenAuditModel event.category.</summary>
    [Required]
    [StringLength(100)]
    public string EventCategory { get; set; } = string.Empty;

    public AuditOutcome Outcome { get; set; }

    /// <summary>OpenAuditModel resource.type.</summary>
    [Required]
    [StringLength(100)]
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>OpenAuditModel resource.id.</summary>
    public Guid? ResourceId { get; set; }

    /// <summary>OpenAuditModel event.summary.</summary>
    [StringLength(500)]
    public string? Summary { get; set; }

    /// <summary>OpenAuditModel change.before, as JSON.</summary>
    public string? ChangeBefore { get; set; }

    /// <summary>OpenAuditModel change.after, as JSON.</summary>
    public string? ChangeAfter { get; set; }

    /// <summary>OpenAuditModel request.ipAddress.</summary>
    [StringLength(50)]
    public string? RequestIpAddress { get; set; }

    /// <summary>OpenAuditModel request.userAgent.</summary>
    [StringLength(500)]
    public string? RequestUserAgent { get; set; }

    /// <summary>OpenAuditModel request.route.</summary>
    [StringLength(500)]
    public string? RequestRoute { get; set; }

    public const int MaxTraceIdLength = 32;
    public const int MaxSpanIdLength = 16;
    public const int MaxCorrelationIdLength = 256;

    /// <summary>The inbound request being served.</summary>
    [StringLength(MaxCorrelationIdLength)]
    public string? RequestId { get; set; }

    /// <summary>The logical operation this event belongs to; stable across traces and messages.</summary>
    [StringLength(MaxCorrelationIdLength)]
    public string? CorrelationId { get; set; }

    /// <summary>W3C trace id, from the active trace context rather than minted here.</summary>
    [StringLength(MaxTraceIdLength)]
    public string? TraceId { get; set; }

    /// <summary>W3C span id; only ever stored alongside a trace id.</summary>
    [StringLength(MaxSpanIdLength)]
    public string? SpanId { get; set; }

    /// <summary>Position in the tamper-evidence chain; null until the sealing job reaches this row.</summary>
    public long? Sequence { get; set; }

    public const int MaxCanonicalizationVersionLength = 32;

    /// <summary>Which canonical form this row's hash was taken over; null means the one before the column existed.</summary>
    // Recorded per row so the field set can grow without making every already-sealed row unverifiable.
    [StringLength(MaxCanonicalizationVersionLength)]
    public string? CanonicalizationVersion { get; set; }

    /// <summary>Chain hash of the preceding entry, base64.</summary>
    [StringLength(64)]
    public string? PrevHash { get; set; }

    /// <summary>Chain hash of this entry, base64.</summary>
    [StringLength(64)]
    public string? RowHash { get; set; }
}
