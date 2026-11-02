using System.Text.Json.Nodes;
using Callu.Domain.Enums;

namespace Callu.Shared.Models.Audit;

/// <summary>The authorization decision that let the audited operation through.</summary>
public sealed record AuditAuthorization(
    string Decision, string? Engine = null, string? Policy = null, IReadOnlyList<string>? Permissions = null)
{
    /// <summary>No request was authorized: a scheduled job, a broker consumer, an anonymous endpoint.</summary>
    public static readonly AuditAuthorization NotApplicable = new("not-applicable");

    /// <summary>An inbound alert accepted on the service's webhook key.</summary>
    public static readonly AuditAuthorization WebhookKey = new("allow", "callu-webhook-key", "service-webhook-key");
}

/// <summary>Why the operation was done, as the person or the source gave it.</summary>
public sealed record AuditReason(string? Code = null, string? Text = null);

public sealed record AuditRelatedResource(string Type, string Id);

/// <summary>What changed, by field name, with values only where they are safe to export.</summary>
public sealed record AuditChange(
    string Type, IReadOnlyList<string> ChangedFields, JsonObject? Before = null, JsonObject? After = null);

/// <summary>The OpenAuditModel fragments only the writer of a row can know.</summary>
public sealed record AuditDetails
{
    public AuditAuthorization? Authorization { get; init; }
    public AuditReason? Reason { get; init; }
    public IReadOnlyList<AuditRelatedResource>? RelatedResources { get; init; }
    public AuditChange? Change { get; init; }

    /// <summary>Namespaced by domain, e.g. <c>{"incident": {...}}</c>.</summary>
    public JsonObject? Metadata { get; init; }
}

/// <summary>Everything a caller hands the audit writer for one row.</summary>
public sealed record AuditRecord(string? UserId, AuditAction Action, string EntityName, string EntityId)
{
    public string? Description { get; init; }
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public AuditDetails? Details { get; init; }
}
