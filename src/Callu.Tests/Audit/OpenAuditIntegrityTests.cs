using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Callu.Domain.Enums;
using Callu.Shared.Audit;
using Callu.Shared.Models.Audit;

namespace Callu.Tests.Audit;

// An outside verifier recalculates the digest from the line it was given, so these tests do the same
// thing it does rather than asking our own code what it computed.
public class OpenAuditIntegrityTests
{
    private static AuditLogDto SealedRow() => new()
    {
        Id = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301"),
        CreatedAt = new DateTime(2026, 8, 5, 9, 12, 33, DateTimeKind.Utc),
        ActorId = "user-1",
        ActorDisplayName = "Ali Gören",
        ActorType = AuditActorType.User,
        Action = AuditAction.Acknowledged,
        EventName = "incident.acknowledged",
        EventCategory = "incident-management",
        Outcome = AuditOutcome.Success,
        ResourceType = "Incident",
        ResourceId = Guid.Parse("0198f1e2-4c3a-7b21-9f55-2a1b3c4d5e6f"),
        Summary = "Incident acknowledged",
        RequestIpAddress = "203.0.113.7",
        RequestRoute = "/api/v1/incidents/3f2504e0/acknowledge",
        Sequence = 2,
        RowHash = Convert.ToBase64String(Enumerable.Repeat((byte)0xAB, 32).ToArray()),
        PrevHash = Convert.ToBase64String(Enumerable.Repeat((byte)0xCD, 32).ToArray()),
    };

    private static string Line(AuditLogDto row) =>
        OpenAuditEventSerializer.ToJsonLine(OpenAuditEventMapper.ToEnvelope(row, "Callu", "production"));

    /// <summary>The digest procedure a verifier runs: drop the two excluded pointers, canonicalize, SHA-256.</summary>
    private static string Recalculate(string line)
    {
        var node = JsonNode.Parse(line)!.AsObject();
        var integrity = node["integrity"]!.AsObject();
        integrity.Remove("hash");
        integrity.Remove("signature");

        var canonical = JsonCanonicalizer.Canonicalize(node.ToJsonString());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    // Pinned from an event the reference OpenAuditModel verifier accepted. Recalculating our own
    // digest only proves we agree with ourselves; this is the byte-for-byte agreement with an outside
    // implementation. Note the unescaped "ö" — RFC 8785 escapes nothing above the control range.
    private const string VerifiedCanonicalForm =
        """{"actor":{"displayName":"Ali Gören","id":"user-1","type":"user"},"application":{"environment":"production","name":"Callu"},"event":{"category":"incident-management","name":"incident.acknowledged","outcome":"success","summary":"Incident acknowledged"},"extensions":{"com.callu.audit.internal-chain.algorithm":"HMAC-SHA256","com.callu.audit.internal-chain.canonicalization":"v2-openauditmodel","com.callu.audit.internal-chain.hash":"abababababababababababababababababababababababababababababababab","com.callu.audit.internal-chain.previous-hash":"cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd"},"id":"3f2504e0-4f89-41d3-9a0c-0305e82c3301","integrity":{"canonicalization":"RFC8785","hashAlgorithm":"SHA-256"},"request":{"ipAddress":"203.0.113.7","route":"/api/v1/incidents/3f2504e0/acknowledge"},"resource":{"id":"0198f1e2-4c3a-7b21-9f55-2a1b3c4d5e6f","type":"incident"},"sequence":2,"specVersion":"0.1","time":"2026-08-05T09:12:33Z"}""";

    private const string VerifiedDigest = "6e381056106446326d8cd03f677746c83007182662c9785012f6ecce87ae1a71";

    [Fact]
    public void CanonicalizesToTheBytesAnExternalVerifierDigested()
    {
        var node = JsonNode.Parse(Line(SealedRow()))!.AsObject();
        node["integrity"]!.AsObject().Remove("hash");

        Assert.Equal(VerifiedCanonicalForm, JsonCanonicalizer.Canonicalize(node.ToJsonString()));
        Assert.Equal(VerifiedDigest, JsonNode.Parse(Line(SealedRow()))!["integrity"]!["hash"]!.GetValue<string>());
    }

    [Fact]
    public void ExportedHashIsADigestOfTheExportedEvent()
    {
        var line = Line(SealedRow());
        var declared = JsonNode.Parse(line)!["integrity"]!["hash"]!.GetValue<string>();

        Assert.Equal(Recalculate(line), declared);
        Assert.Matches("^[0-9a-f]{64}$", declared);
    }

    [Fact]
    public void ChangingASingleFieldBreaksTheDigest()
    {
        var node = JsonNode.Parse(Line(SealedRow()))!.AsObject();
        var declared = node["integrity"]!["hash"]!.GetValue<string>();

        node["actor"]!["id"] = "user-2";

        Assert.NotEqual(declared, Recalculate(node.ToJsonString()));
    }

    // The chain metadata is an assertion the event makes about itself, so rewriting it has to
    // invalidate the event that carries it.
    [Fact]
    public void RewritingTheCarriedChainHashBreaksTheDigest()
    {
        var node = JsonNode.Parse(Line(SealedRow()))!.AsObject();
        var declared = node["integrity"]!["hash"]!.GetValue<string>();

        node["extensions"]!["com.callu.audit.internal-chain.hash"] = new string('0', 64);

        Assert.NotEqual(declared, Recalculate(node.ToJsonString()));
    }

    [Fact]
    public void DeclaresTheAlgorithmAndCanonicalizationAVerifierImplements()
    {
        var integrity = JsonNode.Parse(Line(SealedRow()))!["integrity"]!;

        Assert.Equal("SHA-256", integrity["hashAlgorithm"]!.GetValue<string>());
        Assert.Equal("RFC8785", integrity["canonicalization"]!.GetValue<string>());
    }

    // Chain verification compares previousHash against the predecessor's declared hash. Ours is the
    // digest of a different, keyed document, so publishing it there would fail every time.
    [Fact]
    public void DoesNotDeclareAChainItCannotSubstantiate()
    {
        var integrity = JsonNode.Parse(Line(SealedRow()))!["integrity"]!.AsObject();

        Assert.False(integrity.ContainsKey("previousHash"));
        Assert.False(integrity.ContainsKey("chainId"));
    }

    [Fact]
    public void AnUnsealedRowCarriesNoIntegrityBlockAtAll()
    {
        var row = SealedRow();
        row.RowHash = null;
        row.PrevHash = null;

        var node = JsonNode.Parse(Line(row))!.AsObject();

        Assert.False(node.ContainsKey("integrity"));
        Assert.False(node.ContainsKey("extensions"));
    }

    // An explicit null is not the same as an absent member: the schema treats one as a type error and
    // the other as an unset optional, and the two canonicalize to different bytes.
    [Fact]
    public void OmitsUnsetOptionalsRatherThanNullingThem()
    {
        var row = SealedRow();
        row.Summary = null;
        row.RequestIpAddress = null;
        row.RequestUserAgent = null;
        row.RequestRoute = null;

        var node = JsonNode.Parse(Line(row))!.AsObject();

        Assert.False(node.ContainsKey("request"));
        Assert.False(node["event"]!.AsObject().ContainsKey("summary"));
    }
}
