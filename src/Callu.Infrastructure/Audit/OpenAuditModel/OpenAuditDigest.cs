using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Callu.Shared.Audit;

namespace Callu.Infrastructure.Audit.OpenAuditModel;

/// <summary>The digest OpenAuditModel defines over an event.</summary>
// RFC 8785 over the event with exactly /integrity/hash and /integrity/signature removed, and nothing
// else pruned: a producer that dropped an emptied object and a verifier that kept it would disagree.
public static class OpenAuditDigest
{
    public const string Canonicalization = "RFC8785";
    public const string HashAlgorithm = "SHA-256";

    /// <summary>The bytes the digest and every signature over the event are taken over.</summary>
    public static byte[] Input(JsonObject document)
    {
        var copy = document.DeepClone().AsObject();

        if (copy["integrity"] is JsonObject integrity)
        {
            integrity.Remove("hash");
            integrity.Remove("signature");
        }

        return Encoding.UTF8.GetBytes(JsonCanonicalizer.Canonicalize(copy.ToJsonString()));
    }

    public static string Compute(JsonObject document) =>
        Convert.ToHexStringLower(SHA256.HashData(Input(document)));
}
