using System.Text.RegularExpressions;
using Callu.Domain.Enums;

namespace Callu.Shared.Models.Audit;

/// <summary>Projects Callu's own audit row onto the OpenAuditModel v0.1 event envelope.</summary>
public static partial class OpenAuditEventMapper
{
    private const string UnscopedResourceId = "unscoped";
    private const string SystemActorId = "system";

    /// <summary>Namespace for the internal HMAC chain, which the export carries but cannot be verified against.</summary>
    private const string ChainExtensionPrefix = "com.callu.audit.internal-chain.";

    public static OpenAuditEventEnvelope ToEnvelope(AuditLogDto entry, string applicationName, string environment) => new()
    {
        Id = entry.Id,
        Time = entry.CreatedAt,
        Event = new OpenAuditEventDescriptor
        {
            Name = entry.EventName,
            Category = entry.EventCategory,
            Outcome = entry.Outcome.ToString().ToLowerInvariant(),
            Summary = entry.Summary,
            Error = entry.Outcome is AuditOutcome.Failure or AuditOutcome.Partial
                ? new OpenAuditErrorDescriptor { Code = entry.Action.ToString(), Message = entry.Summary }
                : null,
        },
        Actor = new OpenAuditActor
        {
            Type = entry.ActorType.ToString().ToLowerInvariant(),
            Id = entry.ActorId ?? SystemActorId,
            DisplayName = entry.ActorDisplayName,
        },
        Resource = new OpenAuditResource
        {
            Type = entry.ResourceType.ToLowerInvariant(),
            Id = entry.ResourceId?.ToString() ?? UnscopedResourceId,
        },
        // The schema requires lowercase kebab-case here; ASP.NET's own environment names
        // ("Production", "Development") are not.
        Application = new OpenAuditApplication { Name = applicationName, Environment = environment.ToLowerInvariant() },
        Request = RequestOrNull(entry),
        Change = entry.ChangeBefore is null && entry.ChangeAfter is null
            ? null
            : new OpenAuditChange { Before = entry.ChangeBefore, After = entry.ChangeAfter },
        Sequence = entry.Sequence,
        Extensions = ChainExtensions(entry),
        // Left unsealed here; the serializer fills the digest once the document is complete.
        Integrity = entry.RowHash is null ? null : new OpenAuditIntegrity(),
    };

    /// <summary>The request context, or null when there is nothing to say about how this was requested.</summary>
    // An optional object must carry at least one property, so an entry with no context omits it entirely.
    // A span is dropped without its trace: on its own there is nothing to resolve it against.
    private static OpenAuditRequestContext? RequestOrNull(AuditLogDto entry)
    {
        var traceId = TraceIdOrNull(entry.TraceId);
        var route = RouteOrNull(entry.RequestRoute);

        if (entry.RequestIpAddress is null && entry.RequestUserAgent is null && route is null
            && entry.RequestId is null && entry.CorrelationId is null && traceId is null)
            return null;

        return new OpenAuditRequestContext
        {
            IpAddress = entry.RequestIpAddress,
            UserAgent = entry.RequestUserAgent,
            Route = route,
            RequestId = entry.RequestId,
            CorrelationId = entry.CorrelationId,
            TraceId = traceId,
            SpanId = traceId is null ? null : SpanIdOrNull(entry.SpanId),
        };
    }

    /// <summary>A trace id the schema accepts: 32 lower-case hex characters, never all zero.</summary>
    private static string? TraceIdOrNull(string? traceId) =>
        IsHex(traceId, 32) ? traceId : null;

    private static string? SpanIdOrNull(string? spanId) =>
        IsHex(spanId, 16) ? spanId : null;

    private static bool IsHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.Any(c => c != '0')
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>Carries the internal HMAC chain alongside the export without claiming it is verifiable.</summary>
    // It is keyed, so nobody outside the deployment can recompute it, and it covers a different
    // document from the one exported here. Naming it as this event's integrity would be a false claim.
    private static IReadOnlyDictionary<string, string>? ChainExtensions(AuditLogDto entry)
    {
        if (entry.RowHash is null) return null;

        var extensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ChainExtensionPrefix + "hash"] = ToHex(entry.RowHash),
            [ChainExtensionPrefix + "algorithm"] = CalluAuditChain.HashAlgorithm,
            // The form this row was sealed under, not the one in force today: naming the current one
            // would mislabel every row sealed before it and send a verifier down the wrong field set.
            [ChainExtensionPrefix + "canonicalization"] = CalluAuditChain.VersionOf(entry.CanonicalizationVersion),
        };

        if (entry.PrevHash is not null)
            extensions[ChainExtensionPrefix + "previous-hash"] = ToHex(entry.PrevHash);

        return extensions;
    }

    /// <summary>The route as the schema accepts it, or nothing.</summary>
    // A decoded path can hold a space, and one unusable character would make the whole event fail
    // schema validation — which is the step a verifier does before it looks at the digest at all.
    private static string? RouteOrNull(string? route) =>
        route is not null && SchemaSafeRoute().IsMatch(route) ? route : null;

    [GeneratedRegex("^[^\\s?#]+$")]
    private static partial Regex SchemaSafeRoute();

    private static string ToHex(string base64Hash) =>
        Convert.ToHexStringLower(Convert.FromBase64String(base64Hash));
}
