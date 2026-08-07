using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Callu.Shared.Audit;

namespace Callu.Shared.Models.Audit;

/// <summary>Serializes an OpenAuditModel event and fills its integrity digest.</summary>
public static class OpenAuditEventSerializer
{
    public const string Canonicalization = "RFC8785";
    public const string HashAlgorithm = "SHA-256";

    /// <summary>The one JSON line an export writes for this event, digest included.</summary>
    // The digest is taken over the same node that gets written, so the two cannot drift: a reader
    // recomputes it from the bytes it received, which is the only version it has.
    public static string ToJsonLine(OpenAuditEventEnvelope envelope, IOpenAuditSigner? signer = null)
    {
        var node = JsonSerializer.SerializeToNode(envelope, Options)!.AsObject();

        if (node["integrity"] is JsonObject integrity)
        {
            // Exactly the two pointers the spec excludes, and no pruning of what is left: a producer
            // that dropped an emptied object and a verifier that kept it would disagree on the digest.
            integrity.Remove("hash");
            integrity.Remove("signature");

            // The signature covers the same bytes as the hash, so a signed chain is exactly as
            // tamper-evident as a hashed one.
            var canonical = Encoding.UTF8.GetBytes(JsonCanonicalizer.Canonicalize(node.ToJsonString()));

            integrity["hash"] = Convert.ToHexStringLower(SHA256.HashData(canonical));

            if (signer is not null)
            {
                var signature = new JsonObject
                {
                    ["algorithm"] = signer.Algorithm,
                    ["value"] = signer.Sign(canonical),
                };
                if (signer.KeyId is { Length: > 0 } keyId) signature["keyId"] = keyId;
                integrity["signature"] = signature;
            }
        }

        return node.ToJsonString();
    }

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The schema treats an optional field as either the right type or absent — an explicit
        // null fails validation, so an unset field must be omitted rather than nulled.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
