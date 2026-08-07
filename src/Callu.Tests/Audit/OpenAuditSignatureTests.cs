using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Shared.Audit;
using Callu.Shared.Models.Audit;
using Org.BouncyCastle.Crypto.Parameters;

namespace Callu.Tests.Audit;

// A digest answers "was this changed". Only a signature answers "did Callu produce it" — anyone who
// can recompute a digest can also alter the event and recompute it.
public class OpenAuditSignatureTests
{
    private static (Ed25519Signer Signer, byte[] PublicKey) NewKey(string keyId = "callu-audit-test")
    {
        var seed = RandomNumberGenerator.GetBytes(Ed25519PrivateKeyParameters.KeySize);
        var privateKey = new Ed25519PrivateKeyParameters(seed);

        return (new Ed25519Signer(keyId, seed), privateKey.GeneratePublicKey().GetEncoded());
    }

    private static AuditLogDto SealedRow() => new()
    {
        Id = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301"),
        CreatedAt = new DateTime(2026, 8, 5, 9, 12, 33, DateTimeKind.Utc),
        ActorId = "user-1",
        ActorDisplayName = "Ali Gören",
        ActorType = AuditActorType.User,
        Action = AuditAction.Acknowledged,
        EventName = "incident.acknowledge",
        EventCategory = "incident-management",
        Outcome = AuditOutcome.Success,
        ResourceType = "Incident",
        ResourceId = Guid.Parse("0198f1e2-4c3a-7b21-9f55-2a1b3c4d5e6f"),
        Summary = "Incident acknowledged",
        Sequence = 2,
        RowHash = Convert.ToBase64String(Enumerable.Repeat((byte)0xAB, 32).ToArray()),
    };

    private static string Line(IOpenAuditSigner? signer) =>
        OpenAuditEventSerializer.ToJsonLine(
            OpenAuditEventMapper.ToEnvelope(SealedRow(), "Callu", "production"), signer);

    /// <summary>The bytes a verifier signs over: the canonical event minus hash and signature.</summary>
    private static byte[] DigestInput(string line)
    {
        var node = JsonNode.Parse(line)!.AsObject();
        var integrity = node["integrity"]!.AsObject();
        integrity.Remove("hash");
        integrity.Remove("signature");

        return Encoding.UTF8.GetBytes(JsonCanonicalizer.Canonicalize(node.ToJsonString()));
    }

    // Ed25519 is deterministic, so the same key over the same event always produces these bytes.
    // Pinned from an event the reference OpenAuditModel verifier accepted as schema-valid with its
    // hash intact; the signature itself it cannot check, having no way to be given a public key.
    [Fact]
    public void ProducesTheSignatureItProducedBefore()
    {
        var seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var row = SealedRow();
        row.ActorDisplayName = "Ali Goren";

        var line = OpenAuditEventSerializer.ToJsonLine(
            OpenAuditEventMapper.ToEnvelope(row, "Callu", "production"),
            new Ed25519Signer("callu-audit-20260805-deadbeef", seed));

        var signature = JsonNode.Parse(line)!["integrity"]!["signature"]!;

        Assert.Equal(
            "kbY4YeVyDzxGWNGMtf3cmUFFt3HbRZRVyXfwi4buqMUzKqdxjcV4ix0x8hATWlw7skW2/SvwGJDXxxy3PWWkBA==",
            signature["value"]!.GetValue<string>());
        Assert.Equal(
            "6072c311423a8d2978dcfbfeb9b1b12b7a05820f6d70a474d402afa18d256b93",
            JsonNode.Parse(line)!["integrity"]!["hash"]!.GetValue<string>());
    }

    [Fact]
    public void ExportedEventVerifiesWithThePublishedPublicKey()
    {
        var (signer, publicKey) = NewKey();
        var line = Line(signer);

        var signature = JsonNode.Parse(line)!["integrity"]!["signature"]!;

        Assert.Equal("Ed25519", signature["algorithm"]!.GetValue<string>());
        Assert.Equal("callu-audit-test", signature["keyId"]!.GetValue<string>());
        Assert.True(Ed25519Signer.Verify(publicKey, DigestInput(line), signature["value"]!.GetValue<string>()));
    }

    [Fact]
    public void ChangingTheEventBreaksTheSignature()
    {
        var (signer, publicKey) = NewKey();
        var node = JsonNode.Parse(Line(signer))!.AsObject();
        var signature = node["integrity"]!["signature"]!["value"]!.GetValue<string>();

        node["actor"]!["id"] = "user-2";

        Assert.False(Ed25519Signer.Verify(publicKey, DigestInput(node.ToJsonString()), signature));
    }

    // Rotation retires a key, it does not delete it: everything already exported was signed with the
    // old one and still has to check out.
    [Fact]
    public void ASignatureMadeBeforeRotationStillVerifies()
    {
        var (oldSigner, oldPublicKey) = NewKey("callu-audit-old");
        var line = Line(oldSigner);

        var (_, newPublicKey) = NewKey("callu-audit-new");
        var signature = JsonNode.Parse(line)!["integrity"]!["signature"]!["value"]!.GetValue<string>();

        Assert.True(Ed25519Signer.Verify(oldPublicKey, DigestInput(line), signature));
        Assert.False(Ed25519Signer.Verify(newPublicKey, DigestInput(line), signature));
    }

    // The signature is excluded from the digest, so adding one must not move the hash.
    [Fact]
    public void SigningDoesNotChangeTheDigest()
    {
        var (signer, _) = NewKey();

        var unsigned = JsonNode.Parse(Line(null))!["integrity"]!["hash"]!.GetValue<string>();
        var signed = JsonNode.Parse(Line(signer))!["integrity"]!["hash"]!.GetValue<string>();

        Assert.Equal(unsigned, signed);
    }

    [Fact]
    public void AnUnsignedExportCarriesNoSignatureAtAll()
    {
        var integrity = JsonNode.Parse(Line(null))!["integrity"]!.AsObject();

        Assert.False(integrity.ContainsKey("signature"));
    }

    // Base64 is the one encoding a v0.1 verifier checks; hex would be reported as unverifiable.
    [Fact]
    public void EncodesTheSignatureAsBase64()
    {
        var (signer, _) = NewKey();

        var value = JsonNode.Parse(Line(signer))!["integrity"]!["signature"]!["value"]!.GetValue<string>();

        Assert.Equal(64, Convert.FromBase64String(value).Length);
    }
}
