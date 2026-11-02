using System.Text.Json.Nodes;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit.OpenAuditModel;
using Callu.Shared.Models.Audit;

namespace Callu.Tests.Audit;

/// <summary>One row becomes one 1.0 event that validates and re-hashes to its own digest.</summary>
public class OpenAuditEventBuilderTests
{
    private static readonly OpenAuditSealContext First =
        new(OpenAuditEventSource.Default, "callu-0123456789abcdef0123456789abcdef", "seal-1", null);

    private static AuditLog Row(Action<AuditLog>? edit = null)
    {
        var row = new AuditLog
        {
            Id = Guid.Parse("018f1b70-2c18-7f3a-b46d-5e8a1c9d0b12"),
            CreatedAt = new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc),
            ActorId = "user-1",
            ActorDisplayName = "Ada",
            ActorType = AuditActorType.User,
            Action = AuditAction.Resolved,
            EventName = "incident.resolve",
            EventCategory = "incident-management",
            Outcome = AuditOutcome.Success,
            ResourceType = "Incident",
            ResourceId = Guid.Parse("2f60ff6b-1c3a-4f2e-9c31-6b0a2d5e7f10"),
            Summary = "Incident resolved from Acknowledged",
            RequestRoute = "/api/v{version}/incidents/{id}/resolve",
            RequestMethod = "POST",
            TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
            SpanId = "6f70819203040506",
            ParentSpanId = "00f067aa0ba902b7",
            Sequence = 7,
            RowHash = Convert.ToBase64String(new byte[32]),
            CanonicalizationVersion = "v4-details",
            Details = AuditDetailsJson.Serialize(new AuditDetails
            {
                Authorization = new AuditAuthorization("allow", "aspnetcore-authorization", "CanResolveIncidents", ["CanResolveIncidents"]),
                Reason = new AuditReason(Text: "Disk temizlendi."),
                Metadata = new JsonObject { ["incident"] = new JsonObject { ["status"] = "resolved", ["priority"] = "high", ["resolutionType"] = "permanent-fix" } },
                Change = new AuditChange("update", ["status"], new JsonObject { ["status"] = "acknowledged" }, new JsonObject { ["status"] = "resolved" }),
            }),
        };
        edit?.Invoke(row);
        return row;
    }

    private static JsonObject Sealed(AuditLog row, OpenAuditSealContext? context = null)
    {
        var result = OpenAuditEventBuilder.Seal(row, context ?? First);
        var evt = JsonNode.Parse(result.Canonical)!.AsObject();
        Assert.Equal(result.Hash, evt["integrity"]!["hash"]!.GetValue<string>());
        Assert.Equal(result.Hash, OpenAuditDigest.Compute(evt));
        OamFixtures.AssertValidEvent(evt);
        return evt;
    }

    [Fact]
    public void ANewRow_IsAComplete10Event()
    {
        var evt = Sealed(Row());

        Assert.Equal("1.0", evt["specVersion"]!.GetValue<string>());
        Assert.Equal(7, evt["sequence"]!.GetValue<long>());
        Assert.Equal("2026-10-15T09:30:00Z", evt["time"]!.GetValue<string>());
        Assert.Equal("incident", evt["resource"]!["type"]!.GetValue<string>());
        Assert.Equal("00f067aa0ba902b7", evt["request"]!["parentSpanId"]!.GetValue<string>());
        Assert.Equal("POST", evt["request"]!["method"]!.GetValue<string>());
        Assert.Equal("CanResolveIncidents", evt["authorization"]!["policy"]!.GetValue<string>());
        Assert.Equal("permanent-fix", evt["metadata"]!["incident"]!["resolutionType"]!.GetValue<string>());
        Assert.Equal("callu-0123456789abcdef0123456789abcdef", evt["integrity"]!["chainId"]!.GetValue<string>());
        Assert.Equal("seal-1", evt["integrity"]!["batchId"]!.GetValue<string>());
        Assert.Null(evt["integrity"]!["previousHash"]);
        Assert.Equal("HMAC-SHA256", evt["extensions"]!["com.callu.audit.internal-chain.algorithm"]!.GetValue<string>());
    }

    [Fact]
    public void ALaterRow_LinksToThePreviousHash()
    {
        var previous = new string('c', 64);
        var evt = Sealed(Row(), First with { PreviousHash = previous });

        Assert.Equal(previous, evt["integrity"]!["previousHash"]!.GetValue<string>());
    }

    // The host's environment name is free text; application.environment is a vocabulary token.
    [Theory]
    [InlineData("Production", "production")]
    [InlineData("Prod_EU", "prod-eu")]
    [InlineData("Staging 2", "staging-2")]
    [InlineData("__", "unknown")]
    [InlineData("", "unknown")]
    [InlineData("2nd-site", "unknown")]
    public void TheHostEnvironment_IsNormalizedToAVocabularyToken(string hostEnvironment, string expected)
    {
        var evt = Sealed(Row(), First with { Source = new OpenAuditEventSource("Callu", hostEnvironment) });

        Assert.Equal(expected, evt["application"]!["environment"]!.GetValue<string>());
    }

    [Fact]
    public void AnOverlongHostEnvironment_IsCutToTheTokenLimit()
    {
        var evt = Sealed(Row(), First with { Source = new OpenAuditEventSource("Callu", new string('a', 63) + "_b") });

        Assert.Equal(new string('a', 63), evt["application"]!["environment"]!.GetValue<string>());
    }

    [Fact]
    public void TurkishAndEscapedText_RoundTripsToTheSameDigest()
    {
        var row = Row(r =>
        {
            r.Summary = "Çağrı \"kuyruğu\" \\ taştı — ğüşıöç";
            r.Details = AuditDetailsJson.Serialize(new AuditDetails
            {
                Authorization = AuditAuthorization.NotApplicable,
                Reason = new AuditReason(Text: "Müşteri: \"ödeme sayfası\" çöktü\n"),
            });
        });

        var result = OpenAuditEventBuilder.Seal(row, First);
        var reparsed = JsonNode.Parse(JsonNode.Parse(result.Canonical)!.ToJsonString())!.AsObject();

        Assert.Equal(result.Hash, OpenAuditDigest.Compute(reparsed));
    }

    [Fact]
    public void AFailure_NamesTheActionAsItsErrorCode_AndDoesNotRepeatTheSummary()
    {
        var evt = Sealed(Row(r =>
        {
            r.Action = AuditAction.EscalationNobodyReached;
            r.EventName = "incident.escalation-nobody-reached";
            r.Outcome = AuditOutcome.Failure;
        }));

        Assert.Equal("escalation-nobody-reached", evt["event"]!["error"]!["code"]!.GetValue<string>());
        Assert.Null(evt["event"]!["error"]!["message"]);
    }

    [Fact]
    public void ALegacySystemSentinel_StoredAsAUser_IsProjectedAsASystemActor()
    {
        var evt = Sealed(Row(r =>
        {
            r.Details = null;
            r.ActorId = "system:webhook";
            r.ActorType = AuditActorType.User;
            r.ResourceType = "CommunicationProvider";
        }));

        Assert.Equal("system", evt["actor"]!["type"]!.GetValue<string>());
        Assert.Equal("communication-provider", evt["resource"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void ARowWithDetails_KeepsItsStoredActorType()
    {
        var evt = Sealed(Row(r => { r.ActorId = "system:webhook"; r.ActorType = AuditActorType.User; }));

        Assert.Equal("user", evt["actor"]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Ada ")]
    [InlineData("   ")]
    public void ADisplayNameThePatternRejects_IsOmitted(string name)
    {
        var evt = Sealed(Row(r => r.ActorDisplayName = name));

        Assert.Null(evt["actor"]!["displayName"]);
    }

    [Fact]
    public void AnActorIdWithSurroundingSpace_IsTrimmed_AndAnEmptyOneIsUnknown()
    {
        Assert.Equal("user-1", Sealed(Row(r => r.ActorId = " user-1 "))["actor"]!["id"]!.GetValue<string>());

        var unknown = Sealed(Row(r => r.ActorId = "  "))["actor"]!;
        Assert.Equal("unknown", unknown["id"]!.GetValue<string>());
        Assert.Equal("unknown", unknown["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("fe80::1%3", "fe80::1")]
    [InlineData("not-an-ip", null)]
    [InlineData("203.0.113.7", "203.0.113.7")]
    public void AnIpAddress_IsEmittedOnlyInAFormTheSchemaAccepts(string stored, string? expected)
    {
        var evt = Sealed(Row(r => r.RequestIpAddress = stored));

        Assert.Equal(expected, evt["request"]!["ipAddress"]?.GetValue<string>());
    }

    [Fact]
    public void ARequestIdWithTrailingSpace_IsOmitted()
    {
        var evt = Sealed(Row(r => { r.RequestId = "req-1 "; r.CorrelationId = "corr-1"; }));

        Assert.Null(evt["request"]!["requestId"]);
        Assert.Equal("corr-1", evt["request"]!["correlationId"]!.GetValue<string>());
    }

    [Fact]
    public void APartialOutcome_CarriesAnErrorCode()
    {
        var evt = Sealed(Row(r =>
        {
            r.Action = AuditAction.EscalationDispatchPartiallyFailed;
            r.EventName = "incident.escalation-dispatch-partially-failed";
            r.Outcome = AuditOutcome.Partial;
        }));

        Assert.Equal("partial", evt["event"]!["outcome"]!.GetValue<string>());
        Assert.Equal("escalation-dispatch-partially-failed", evt["event"]!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void ARowWithNoRequestContext_HasNoRequestObject()
    {
        var evt = Sealed(Row(r =>
        {
            r.RequestRoute = null; r.RequestMethod = null; r.TraceId = null; r.SpanId = null; r.ParentSpanId = null;
        }));

        Assert.False(evt.ContainsKey("request"));
    }

    [Fact]
    public void ASpanWithoutATrace_AndAParentWithoutASpan_AreDropped()
    {
        var noTrace = Sealed(Row(r => r.TraceId = null))["request"]!;
        Assert.Null(noTrace["spanId"]);
        Assert.Null(noTrace["parentSpanId"]);

        var noSpan = Sealed(Row(r => r.SpanId = null))["request"]!;
        Assert.NotNull(noSpan["traceId"]);
        Assert.Null(noSpan["parentSpanId"]);
    }

    [Fact]
    public void ALegacyRow_IsProjected_NotTrusted()
    {
        var evt = Sealed(Row(r =>
        {
            r.Details = null;
            r.RequestMethod = null;
            r.ParentSpanId = null;
            r.RequestRoute = "/api/v1/sip-trunks/2f60ff6b-1c3a-4f2e-9c31-6b0a2d5e7f10";
            r.ChangeBefore = "name=old; server=10.0.0.1:5060; user=pbx";
            r.ChangeAfter = "name=new; server=10.0.0.2:5060; user=pbx";
        }));

        Assert.Equal("/api/v1/sip-trunks/{id}", evt["request"]!["route"]!.GetValue<string>());
        Assert.Null(evt["authorization"]);
        Assert.Equal(["name", "server", "user"],
            evt["change"]!["changedFields"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Null(evt["change"]!["before"]);
        Assert.Null(evt["change"]!["after"]);
    }

    [Theory]
    [InlineData("/api/v1/incidents/2f60ff6b-1c3a-4f2e-9c31-6b0a2d5e7f10/resolve", "/api/v1/incidents/{id}/resolve")]
    [InlineData("/api/v1/audit-logs/export", "/api/v1/audit-logs/export")]
    [InlineData("/api/v1/providers/capability-routes/VideoConference", null)]
    [InlineData("/api/v1/search/some value", null)]
    [InlineData("/", "/")]
    [InlineData(null, null)]
    public void LegacyRoutes_AreTemplatedOrDropped(string? route, string? expected) =>
        Assert.Equal(expected, OpenAuditEventBuilder.LegacyRoute(route));

    [Theory]
    [InlineData("Status: Open", new[] { "status" })]
    [InlineData("format=jsonl; from=; to=", new[] { "format", "from", "to" })]
    [InlineData("""{"Title":"x","Severity":2}""", new[] { "title", "severity" })]
    [InlineData("[High] Database down", new string[0])]
    [InlineData("""{"URL":"x","ID":1,"StatusCode":2}""", new[] { "url", "id", "statusCode" })]
    public void LegacyChangeStrings_YieldFieldNamesOnly(string value, string[] expected) =>
        Assert.Equal(expected, OpenAuditEventBuilder.FieldNames(value));

    [Fact]
    public void ADuplicateKeyInAJsonChange_DoesNotThrow() =>
        Assert.NotNull(OpenAuditEventBuilder.FieldNames("""{"a":1,"a":2}"""));

    [Fact]
    public void AnUnparseableLegacyChange_IsOther()
    {
        var evt = Sealed(Row(r => { r.Details = null; r.ChangeAfter = "[High] Database down"; r.ChangeBefore = null; }));

        Assert.Equal("other", evt["change"]!["type"]!.GetValue<string>());
    }
}
