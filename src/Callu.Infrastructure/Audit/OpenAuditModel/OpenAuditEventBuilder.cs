using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Audit;
using Callu.Shared.Models.Audit;

namespace Callu.Infrastructure.Audit.OpenAuditModel;

/// <summary>Which application and environment a stored event says it came from.</summary>
public sealed record OpenAuditEventSource(string ApplicationName, string Environment)
{
    public static readonly OpenAuditEventSource Default = new("Callu", "production");
}

/// <summary>Where in the public chain the row being sealed goes.</summary>
public sealed record OpenAuditSealContext(OpenAuditEventSource Source, string ChainId, string BatchId, string? PreviousHash);

/// <summary>The stored event text and its digest.</summary>
public sealed record OpenAuditSealedEvent(string Canonical, string Hash);

/// <summary>Builds the OpenAuditModel 1.0 event for one audit row, once, at seal time.</summary>
// Built once and stored, because a digest is a claim about bytes: built at export time, the same row
// would hash differently whenever this code changed. Rows written before 1.3.0 have no Details and are
// projected — routes templated or dropped, change reduced to field names — never given fields they
// did not record.
public static partial class OpenAuditEventBuilder
{
    public const string SpecVersion = "1.0";

    private const string UnscopedResourceId = "unscoped";
    private const string SystemActorId = "system";
    private const string UnknownActorId = "unknown";
    private const string ChainExtensionPrefix = "com.callu.audit.internal-chain.";
    private const int MaxChangedFields = 128;
    private const int MaxIdentifierLength = 256;
    private const int MaxShortIdentifierLength = 128;
    private const int MaxUserAgentLength = 512;
    private const int MaxRouteLength = 512;
    private const int MaxEnvironmentLength = 64;
    private const string UnknownEnvironment = "unknown";

    private static readonly string[] DetailKeys = ["authorization", "reason", "relatedResources", "metadata", "change"];

    public static OpenAuditSealedEvent Seal(AuditLog row, OpenAuditSealContext context)
    {
        var document = Build(row, context);
        var hash = OpenAuditDigest.Compute(document);
        document["integrity"]!.AsObject()["hash"] = hash;

        return new OpenAuditSealedEvent(JsonCanonicalizer.Canonicalize(document.ToJsonString()), hash);
    }

    internal static JsonObject Build(AuditLog row, OpenAuditSealContext context)
    {
        var details = row.Details is null ? null : JsonNode.Parse(row.Details)!.AsObject();

        var document = new JsonObject
        {
            ["specVersion"] = SpecVersion,
            ["id"] = row.Id.ToString("D"),
            ["time"] = OpenAuditTime.Format(row.CreatedAt),
            ["sequence"] = row.Sequence!.Value,
            ["event"] = Event(row),
            ["actor"] = Actor(row, legacy: details is null),
            ["resource"] = new JsonObject
            {
                ["type"] = AuditEventNaming.ToResourceType(row.ResourceType),
                ["id"] = row.ResourceId?.ToString("D") ?? UnscopedResourceId,
            },
            ["application"] = new JsonObject
            {
                ["name"] = context.Source.ApplicationName,
                ["environment"] = EnvironmentToken(context.Source.Environment),
            },
        };

        if (Request(row, legacy: details is null) is { } request) document["request"] = request;

        if (details is not null)
            foreach (var key in DetailKeys)
                if (details[key] is { } value) document[key] = value.DeepClone();

        if (document["change"] is null && ProjectedChange(row) is { } change) document["change"] = change;
        if (ChainExtensions(row) is { } extensions) document["extensions"] = extensions;

        var integrity = new JsonObject
        {
            ["canonicalization"] = OpenAuditDigest.Canonicalization,
            ["hashAlgorithm"] = OpenAuditDigest.HashAlgorithm,
            ["chainId"] = context.ChainId,
            ["batchId"] = context.BatchId,
        };
        // The first event of a chain omits previousHash; the specification defines no genesis value.
        if (context.PreviousHash is not null) integrity["previousHash"] = context.PreviousHash;
        document["integrity"] = integrity;

        return document;
    }

    private static JsonObject Event(AuditLog row)
    {
        var evt = new JsonObject
        {
            ["name"] = row.EventName,
            ["category"] = row.EventCategory,
            ["outcome"] = row.Outcome.ToString().ToLowerInvariant(),
        };
        if (!string.IsNullOrEmpty(row.Summary)) evt["summary"] = row.Summary;
        // The code names what failed; the summary already says it in words, so it is not repeated.
        if (row.Outcome is AuditOutcome.Failure or AuditOutcome.Partial)
            evt["error"] = new JsonObject { ["code"] = AuditEventNaming.ToActionSegment(row.Action) };
        return evt;
    }

    private static JsonObject Actor(AuditLog row, bool legacy)
    {
        var type = row.ActorType.ToString().ToLowerInvariant();
        // A row written before 1.3.0 may call a system sentinel a user; the id says which it was.
        if (legacy && row.ActorType == AuditActorType.User && AuditRequestContext.ActorType(row.ActorId) == AuditActorType.System)
            type = "system";

        string id;
        if (row.ActorId is null) id = SystemActorId;
        else if ((Identifier(row.ActorId, MaxIdentifierLength) ?? Identifier(row.ActorId.Trim(), MaxIdentifierLength)) is { } valid) id = valid;
        else
        {
            // Callu's writers never produce this; the schema still needs a principal that is something.
            id = UnknownActorId;
            type = "unknown";
        }

        var actor = new JsonObject { ["type"] = type, ["id"] = id };
        Add(actor, "displayName", Identifier(row.ActorDisplayName, MaxShortIdentifierLength));
        return actor;
    }

    /// <summary>The value when it satisfies the schema's identifier pattern and length, else nothing.</summary>
    // Copied from a column that never enforced the schema's rule against surrounding whitespace.
    private static string? Identifier(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length <= maxLength && IdentifierPattern().IsMatch(value) ? value : null;

    /// <summary>Whether the value satisfies the schema's identifier pattern: no surrounding whitespace, no line break.</summary>
    internal static bool IsIdentifier(string value) => IdentifierPattern().IsMatch(value);

    /// <summary>The address in the form the schema's ipv4/ipv6 formats accept, else nothing.</summary>
    private static string? IpAddress(string? value)
    {
        if (!IPAddress.TryParse(value, out var address)) return null;
        // A zone id is not part of an address the format accepts.
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            address = new IPAddress(address.GetAddressBytes());
        return address.ToString();
    }

    private static JsonObject? Request(AuditLog row, bool legacy)
    {
        var traceId = IsHex(row.TraceId, 32) ? row.TraceId : null;
        var spanId = traceId is not null && IsHex(row.SpanId, 16) ? row.SpanId : null;
        var parentSpanId = spanId is not null && IsHex(row.ParentSpanId, 16) ? row.ParentSpanId : null;
        var route = legacy ? LegacyRoute(row.RequestRoute) : SafeRoute(row.RequestRoute);
        var method = row.RequestMethod is { Length: > 0 } m && Method().IsMatch(m) ? m : null;

        var request = new JsonObject();
        Add(request, "requestId", Identifier(row.RequestId, MaxIdentifierLength));
        Add(request, "correlationId", Identifier(row.CorrelationId, MaxIdentifierLength));
        Add(request, "traceId", traceId);
        Add(request, "spanId", spanId);
        Add(request, "parentSpanId", parentSpanId);
        Add(request, "ipAddress", IpAddress(row.RequestIpAddress));
        Add(request, "userAgent", row.RequestUserAgent is { Length: > 0 and <= MaxUserAgentLength } ? row.RequestUserAgent : null);
        Add(request, "method", method);
        Add(request, "route", route);

        // An optional object must carry at least one property, so a row with no context omits it.
        return request.Count == 0 ? null : request;
    }

    /// <summary>A pre-1.3.0 resolved path as a template, or nothing when it cannot be made one safely.</summary>
    internal static string? LegacyRoute(string? route)
    {
        if (SafeRoute(route) is not { } safe) return null;

        var templated = GuidSegment().Replace(safe, "{id}");
        var segments = templated.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.All(s => s == "{id}" || KebabOrVersion().IsMatch(s)) ? templated : null;
    }

    /// <summary>Field names a pre-1.3.0 change string names; never its values.</summary>
    internal static IReadOnlyList<string> FieldNames(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var trimmed = value.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                if (JsonNode.Parse(trimmed) is JsonObject json)
                    return json.Select(p => Camel(p.Key)).Where(IsFieldName).ToList();
            }
            catch (Exception e) when (e is JsonException or ArgumentException)
            {
                // Not JSON after all; read it as key=value text below.
            }
        }

        return trimmed.Split(';')
            .Select(part => part.IndexOfAny(['=', ':']) is var at and > 0 ? part[..at].Trim() : null)
            .OfType<string>()
            .Where(IsFieldName)
            .Select(Camel)
            .ToList();
    }

    private static JsonObject? ProjectedChange(AuditLog row)
    {
        if (row.ChangeBefore is null && row.ChangeAfter is null) return null;

        var fields = FieldNames(row.ChangeBefore)
            .Concat(FieldNames(row.ChangeAfter))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxChangedFields)
            .ToList();

        return fields.Count == 0
            ? new JsonObject { ["type"] = "other" }
            : new JsonObject { ["changedFields"] = new JsonArray(fields.Select(f => (JsonNode?)f).ToArray()) };
    }

    /// <summary>The private HMAC chain's values, carried so a reader can match this event to it.</summary>
    // Keyed, so nobody outside the deployment can recompute it; it is a cross-reference, not this
    // event's integrity, which is why it sits under a vendor namespace.
    private static JsonObject? ChainExtensions(AuditLog row)
    {
        if (row.RowHash is null) return null;

        var extensions = new JsonObject
        {
            [ChainExtensionPrefix + "hash"] = ToHex(row.RowHash),
            [ChainExtensionPrefix + "algorithm"] = CalluAuditChain.HashAlgorithm,
            [ChainExtensionPrefix + "canonicalization"] = CalluAuditChain.VersionOf(row.CanonicalizationVersion),
        };
        if (row.PrevHash is not null) extensions[ChainExtensionPrefix + "previous-hash"] = ToHex(row.PrevHash);
        return extensions;
    }

    private static void Add(JsonObject target, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) target[key] = value;
    }

    private static string? SafeRoute(string? route) =>
        route is { Length: <= MaxRouteLength } && Route().IsMatch(route) ? route : null;

    private static bool IsHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.Any(c => c != '0')
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsFieldName(string name) => FieldName().IsMatch(name);

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string ToHex(string base64) => Convert.ToHexStringLower(Convert.FromBase64String(base64));

    [GeneratedRegex("^\\S([^\\n\\r\\u2028\\u2029]*\\S)?\\z")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[^\\s?#]+$")]
    private static partial Regex Route();

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidSegment();

    /// <summary>The host's environment name as the schema's vocabulary token.</summary>
    // ASP.NET names environments "Production", and an operator may name one "Prod_EU"; the token is
    // lower-case kebab-case, starts with a letter and stops at 64 characters.
    internal static string EnvironmentToken(string environment)
    {
        var token = NotTokenCharacters().Replace(environment.ToLowerInvariant(), "-").Trim('-');
        if (token.Length > MaxEnvironmentLength) token = token[..MaxEnvironmentLength].TrimEnd('-');

        return token.Length > 0 && char.IsAsciiLetterLower(token[0]) ? token : UnknownEnvironment;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotTokenCharacters();

    [GeneratedRegex("^(v[0-9]+|[a-z0-9]+(-[a-z0-9]+)*)$")]
    private static partial Regex KebabOrVersion();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex FieldName();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,31}$")]
    private static partial Regex Method();
}
