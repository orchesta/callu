using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Callu.Domain.Entities;
using Callu.Shared.Models.Audit;

namespace Callu.Infrastructure.Audit;

/// <summary>Computes the chain hash of an audit entry.</summary>
public static class AuditChainHash
{
    public const int KeySizeBytes = 32;

    public static string Compute(AuditLog entry, long sequence, string? prevHash, byte[] key, string version)
    {
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(
            Canonical(entry, sequence, prevHash, version))));
    }

    /// <summary>The exact bytes the hash is taken over: a sorted-key JSON object with no
    /// insignificant whitespace. The payload is one flat, scalar-valued level, so this satisfies
    /// RFC 8785's member-ordering rule without needing its full recursive number/string formatting —
    /// it has not been checked against RFC 8785 test vectors byte-for-byte.</summary>
    internal static string Canonical(AuditLog entry, long sequence, string? prevHash, string version)
    {
        var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["actorDisplayName"] = entry.ActorDisplayName,
            ["actorId"] = entry.ActorId,
            ["actorType"] = entry.ActorType.ToString(),
            ["changeAfter"] = entry.ChangeAfter,
            ["changeBefore"] = entry.ChangeBefore,
            ["eventAction"] = entry.Action.ToString(),
            ["eventCategory"] = entry.EventCategory,
            ["eventName"] = entry.EventName,
            ["id"] = entry.Id.ToString("D"),
            ["outcome"] = entry.Outcome.ToString(),
            ["prevHash"] = prevHash,
            ["requestIpAddress"] = entry.RequestIpAddress,
            ["requestRoute"] = entry.RequestRoute,
            ["requestUserAgent"] = entry.RequestUserAgent,
            ["resourceId"] = entry.ResourceId?.ToString("D"),
            ["resourceType"] = entry.ResourceType,
            ["sequence"] = sequence.ToString(CultureInfo.InvariantCulture),
            ["specVersion"] = version,
            ["summary"] = entry.Summary,
            ["time"] = StorableInstant(entry.CreatedAt).ToString("O", CultureInfo.InvariantCulture),
        };

        // Added in this form, absent from the one before it: the ids saying which request and which
        // trace an event belongs to are as re-writable as the address it came from, which is sealed.
        if (version != CalluAuditChain.LegacyCanonicalizationId)
        {
            fields["correlationId"] = entry.CorrelationId;
            fields["requestId"] = entry.RequestId;
            fields["spanId"] = entry.SpanId;
            fields["traceId"] = entry.TraceId;
        }

        var jsonObject = new JsonObject();
        foreach (var (key, value) in fields)
            jsonObject[key] = value is null ? null : JsonValue.Create(value);

        return jsonObject.ToJsonString();
    }

    /// <summary>The timestamp as the database can give it back: UTC, microsecond resolution.</summary>
    // Postgres keeps microseconds, .NET keeps 100ns ticks. Hashing the finer value seals a moment
    // the row can never be read back at, and the entry then reads as tampered with.
    private static DateTime StorableInstant(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond), DateTimeKind.Utc);
    }

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySizeBytes);
}
