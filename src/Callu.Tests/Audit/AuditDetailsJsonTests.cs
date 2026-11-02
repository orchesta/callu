using System.Text.Json.Nodes;
using Callu.Shared.Models.Audit;

namespace Callu.Tests.Audit;

/// <summary>The Details column holds OpenAuditModel fragments in their own shape, nothing empty.</summary>
public class AuditDetailsJsonTests
{
    [Fact]
    public void EveryFragment_IsWrittenInTheShapeTheModelDefines()
    {
        var details = new AuditDetails
        {
            Authorization = new AuditAuthorization("allow", "aspnetcore-authorization", "CanResolveIncidents", ["CanResolveIncidents"]),
            Reason = new AuditReason(Text: "Müşteri bildirdi"),
            RelatedResources = [new AuditRelatedResource("service", "svc-1")],
            Change = new AuditChange("update", ["status"],
                new JsonObject { ["status"] = "open" }, new JsonObject { ["status"] = "resolved" }),
            Metadata = new JsonObject { ["incident"] = new JsonObject { ["status"] = "resolved" } },
        };

        var json = JsonNode.Parse(AuditDetailsJson.Serialize(details)!)!.AsObject();

        Assert.Equal("allow", json["authorization"]!["decision"]!.GetValue<string>());
        Assert.Equal("CanResolveIncidents", json["authorization"]!["permissions"]![0]!.GetValue<string>());
        Assert.Equal("Müşteri bildirdi", json["reason"]!["text"]!.GetValue<string>());
        Assert.Null(json["reason"]!["code"]);
        Assert.Equal("service", json["relatedResources"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("resolved", json["change"]!["after"]!["status"]!.GetValue<string>());
        Assert.Equal("resolved", json["metadata"]!["incident"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void NothingToSay_IsNull()
    {
        Assert.Null(AuditDetailsJson.Serialize(null));
        Assert.Null(AuditDetailsJson.Serialize(new AuditDetails()));
        Assert.Null(AuditDetailsJson.Serialize(new AuditDetails { Reason = new AuditReason() }));
    }
}
