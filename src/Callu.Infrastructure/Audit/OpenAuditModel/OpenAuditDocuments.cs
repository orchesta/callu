using System.Text;
using System.Text.Json.Nodes;
using Callu.Shared.Audit;
using Callu.Shared.Models.Audit;

namespace Callu.Infrastructure.Audit.OpenAuditModel;

/// <summary>OpenAuditModel's checkpoint 0.1 and proof 0.1 documents.</summary>
// Each signature covers the canonical form of its own object with /signature removed, which is the
// form before the signature is added; a root is signed once and serves every proof cut from its tree.
public static class OpenAuditDocuments
{
    public const string CheckpointVersion = "0.1";
    public const string ProofVersion = "0.1";

    public static JsonObject Anchor(string type, string reference) => new()
    {
        ["type"] = type,
        ["reference"] = reference,
    };

    public static JsonObject Checkpoint(
        string chainId, long headSequence, string headHash, long eventCount,
        DateTime createdAt, JsonObject anchor, IOpenAuditSigner? signer) =>
        Signed(new JsonObject
        {
            ["checkpointVersion"] = CheckpointVersion,
            ["chainId"] = chainId,
            ["hashAlgorithm"] = OpenAuditDigest.HashAlgorithm,
            ["canonicalization"] = OpenAuditDigest.Canonicalization,
            ["head"] = new JsonObject { ["sequence"] = headSequence, ["hash"] = headHash },
            ["eventCount"] = eventCount,
            ["createdAt"] = OpenAuditTime.Format(createdAt),
            ["anchor"] = anchor.DeepClone(),
        }, signer);

    public static JsonObject Root(string rootHash, long leafCount, JsonObject anchor, IOpenAuditSigner? signer) =>
        Signed(new JsonObject
        {
            ["hash"] = rootHash,
            ["leafCount"] = leafCount,
            ["anchor"] = anchor.DeepClone(),
        }, signer);

    public static JsonObject Proof(
        string eventHash, long index, Guid eventId,
        IReadOnlyList<MerklePathStep> path, JsonObject signedRoot) => new()
    {
        ["proofVersion"] = ProofVersion,
        ["hashAlgorithm"] = OpenAuditDigest.HashAlgorithm,
        ["leaf"] = new JsonObject
        {
            ["hash"] = eventHash,
            ["index"] = index,
            ["eventId"] = eventId.ToString("D"),
        },
        ["path"] = new JsonArray(path
            .Select(step => (JsonNode?)new JsonObject
            {
                ["side"] = step.Side,
                ["hash"] = Convert.ToHexStringLower(step.Hash),
            })
            .ToArray()),
        ["root"] = signedRoot.DeepClone(),
    };

    public static string Canonical(JsonNode node) => JsonCanonicalizer.Canonicalize(node.ToJsonString());

    private static JsonObject Signed(JsonObject document, IOpenAuditSigner? signer)
    {
        if (signer is null) return document;

        var signature = new JsonObject
        {
            ["algorithm"] = signer.Algorithm,
            ["value"] = signer.Sign(Encoding.UTF8.GetBytes(Canonical(document))),
        };
        if (signer.KeyId is { Length: > 0 } keyId) signature["keyId"] = keyId;

        document["signature"] = signature;
        return document;
    }
}
