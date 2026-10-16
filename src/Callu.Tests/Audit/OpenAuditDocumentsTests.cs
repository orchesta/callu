using System.Text.Json.Nodes;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Audit.OpenAuditModel;

namespace Callu.Tests.Audit;

/// <summary>callu's checkpoint, root and proof documents are byte-identical to the reference's.</summary>
// Ed25519 is deterministic, so with the reference's published test seed the signatures must match
// to the byte. A mismatch here means a verifier would reject what callu issues.
public class OpenAuditDocumentsTests
{
    private static Ed25519Signer TestSigner() => new(OamFixtures.TestKeyId, OamFixtures.TestSeed);

    private static List<byte[]> ReferenceDigests() =>
        new[] { "001.json", "002.json", "003.json" }
            .Select(f => Convert.FromHexString(OamFixtures.Read(f)["integrity"]!["hash"]!.GetValue<string>()))
            .ToList();

    [Fact]
    public void TheCheckpoint_IsTheReferenceCheckpoint_SignatureIncluded()
    {
        var fixture = OamFixtures.Read("three-event-chain.checkpoint.json");

        var built = OpenAuditDocuments.Checkpoint(
            chainId: "chain-platform-control-service-instance-7c1a",
            headSequence: 3,
            headHash: "6e066fd770f79d596f97f36dba3dd5be6348acd73d1ec21cc3dd899ec389d186",
            eventCount: 3,
            createdAt: new DateTime(2026, 4, 3, 10, 0, 0, DateTimeKind.Utc),
            anchor: fixture["anchor"]!.AsObject(),
            signer: TestSigner());

        Assert.Equal(OamFixtures.Canonical(fixture), OamFixtures.Canonical(built));
        OamFixtures.AssertValidCheckpoint(built);
    }

    [Fact]
    public void TheProof_IsTheReferenceProof_RootSignatureIncluded()
    {
        var fixture = OamFixtures.Read("three-event-chain.002.proof.json");
        var digests = ReferenceDigests();

        var root = OpenAuditDocuments.Root(
            Convert.ToHexStringLower(MerkleTree.Root(digests)), digests.Count,
            fixture["root"]!["anchor"]!.AsObject(), TestSigner());

        var proof = OpenAuditDocuments.Proof(
            eventHash: Convert.ToHexStringLower(digests[1]),
            index: 1,
            eventId: Guid.Parse(fixture["leaf"]!["eventId"]!.GetValue<string>()),
            path: MerkleTree.AuditPath(digests, 1),
            signedRoot: root);

        Assert.Equal(OamFixtures.Canonical(fixture), OamFixtures.Canonical(proof));
        OamFixtures.AssertValidProof(proof);
    }

    [Fact]
    public void WithoutASigner_TheDocumentsCarryNoSignature()
    {
        var anchor = OpenAuditDocuments.Anchor("manual", "CHG-1");

        var checkpoint = OpenAuditDocuments.Checkpoint("callu-x", 1, new string('a', 64), 1, DateTime.UtcNow, anchor, null);
        var root = OpenAuditDocuments.Root(new string('b', 64), 1, anchor, null);

        Assert.Null(checkpoint["signature"]);
        Assert.Null(root["signature"]);
        OamFixtures.AssertValidCheckpoint(checkpoint);
    }

    [Fact]
    public void TheAnchorPassedIn_IsNotSharedWithTheDocument()
    {
        var anchor = OpenAuditDocuments.Anchor("manual", "CHG-1");
        var checkpoint = OpenAuditDocuments.Checkpoint("callu-x", 1, new string('a', 64), 1, DateTime.UtcNow, anchor, null);

        anchor["reference"] = "changed";

        Assert.Equal("CHG-1", checkpoint["anchor"]!["reference"]!.GetValue<string>());
    }
}
