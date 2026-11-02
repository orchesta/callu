using System.Text.Json.Nodes;
using Callu.Shared.Audit;
using Json.Schema;

namespace Callu.Tests.Audit;

/// <summary>OpenAuditModel's own published files, read from the test output.</summary>
// callu's digest, tree and signature code is checked against what the reference published. Checked
// only against itself, a shared mistake on both sides would pass.
internal static class OamFixtures
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "Audit", "Fixtures", "OpenAuditModel");

    public static JsonObject Read(string file) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, file)))!.AsObject();

    public static string Canonical(JsonNode node) => JsonCanonicalizer.Canonicalize(node.ToJsonString());

    /// <summary>OpenAuditModel's published test seed; public by design, see Fixtures/OpenAuditModel/NOTICE.md.</summary>
    public static byte[] TestSeed => Convert.FromBase64String("1IUTES5grHravR9DoRC5lm6cOPfhbYxwBnc4B/yrP1I=");

    public const string TestKeyId = "example-fixture-key-2026";

    private static readonly Lazy<(JsonSchema Event, JsonSchema Checkpoint, JsonSchema Proof)> Schemas = new(() =>
    {
        // The checkpoint and proof schemas borrow definitions from the 0.1 event schema by its URL,
        // so it is registered first even though callu writes 1.0 events.
        SchemaRegistry.Global.Register(Load("audit-event-0.1.schema.json"));
        var evt = Load("audit-event-1.0.schema.json");
        SchemaRegistry.Global.Register(evt);
        // The proof schema in turn borrows from the checkpoint schema.
        var checkpoint = Load("checkpoint-0.1.schema.json");
        SchemaRegistry.Global.Register(checkpoint);
        return (evt, checkpoint, Load("proof-0.1.schema.json"));
    });

    // JsonSchema.Net rejects the self-referencing jsonValue definition ("any JSON value") as a circular
    // reference. The definition accepts every JSON value, which is what the schema `true` does, so
    // it is swapped in memory; the vendored files themselves stay byte-for-byte as published.
    private static JsonSchema Load(string file)
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, file)))!.AsObject();
        if (node["$defs"] is JsonObject defs && defs.ContainsKey("jsonValue"))
            defs["jsonValue"] = JsonNode.Parse("true");
        return JsonSchema.FromText(node.ToJsonString());
    }

    public static void AssertValidEvent(JsonNode node) => AssertValid(Schemas.Value.Event, node);
    public static void AssertValidCheckpoint(JsonNode node) => AssertValid(Schemas.Value.Checkpoint, node);
    public static void AssertValidProof(JsonNode node) => AssertValid(Schemas.Value.Proof, node);

    private static void AssertValid(JsonSchema schema, JsonNode node)
    {
        var result = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        var errors = (result.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"));
        Assert.True(result.IsValid, string.Join("; ", errors));
    }
}
