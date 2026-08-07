using Callu.Domain.Enums;

namespace Callu.Shared.Models.Audit;

/// <summary>One audit row as an auditor reads it.</summary>
public class AuditLogDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ActorId { get; set; }
    public string? ActorDisplayName { get; set; }
    public AuditActorType ActorType { get; set; }
    public AuditAction Action { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string EventCategory { get; set; } = string.Empty;
    public AuditOutcome Outcome { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public string? Summary { get; set; }
    public string? ChangeBefore { get; set; }
    public string? ChangeAfter { get; set; }
    public string? RequestIpAddress { get; set; }
    public string? RequestUserAgent { get; set; }
    public string? RequestRoute { get; set; }
    public string? RequestId { get; set; }
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }

    /// <summary>Position in the tamper-evidence chain, null until the row is sealed.</summary>
    public long? Sequence { get; set; }

    public string? PrevHash { get; set; }
    public string? RowHash { get; set; }

    /// <summary>Which canonical form this row's hash was taken over.</summary>
    public string? CanonicalizationVersion { get; set; }
}

/// <summary>What an auditor narrows the trail down by.</summary>
public class AuditLogFilter
{
    /// <summary>Inclusive lower bound, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound, UTC.</summary>
    public DateTime? To { get; set; }

    public string? ActorId { get; set; }
    public AuditAction? Action { get; set; }
    public string? ResourceType { get; set; }

    /// <summary>Matches any of several resource types.</summary>
    public List<string>? ResourceTypes { get; set; }

    /// <summary>Reads oldest first, for a trail meant to be read as a sequence of events.</summary>
    public bool SortAscending { get; set; }

    public Guid? ResourceId { get; set; }
    public string? RequestIpAddress { get; set; }

    /// <summary>Matches the actor name, the summary, or the request route.</summary>
    public string? Query { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
