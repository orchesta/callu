using System.Globalization;
using System.Text.Json.Nodes;
using Callu.Infrastructure.Audit.OpenAuditModel;

namespace Callu.Tests.Audit;

/// <summary>callu's digest is the one OpenAuditModel publishes for the same event.</summary>
public class OpenAuditDigestTests
{
    [Theory]
    [InlineData("001.json")]
    [InlineData("002.json")]
    [InlineData("003.json")]
    public void TheDigestOfAReferenceEvent_IsTheOneItDeclares(string file)
    {
        var evt = OamFixtures.Read(file);

        Assert.Equal(evt["integrity"]!["hash"]!.GetValue<string>(), OpenAuditDigest.Compute(evt));
    }

    [Fact]
    public void TheDigestLeavesOutTheHashAndTheSignature()
    {
        var evt = OamFixtures.Read("002.json");
        var before = OpenAuditDigest.Compute(evt);

        evt["integrity"]!["hash"] = "00";
        evt["integrity"]!.AsObject()["signature"] = new JsonObject { ["algorithm"] = "Ed25519", ["value"] = "AA==" };

        Assert.Equal(before, OpenAuditDigest.Compute(evt));
    }

    [Fact]
    public void TheVendoredReferenceEvents_AreValidUnderTheVendoredSchema()
    {
        foreach (var file in new[] { "001.json", "002.json", "003.json" })
            OamFixtures.AssertValidEvent(OamFixtures.Read(file));
    }

    [Theory]
    [InlineData("2026-04-03T10:00:00.0000000Z", "2026-04-03T10:00:00Z")]
    [InlineData("2026-03-14T11:47:52.1080000Z", "2026-03-14T11:47:52.108Z")]
    [InlineData("2026-03-14T11:47:52.1234567Z", "2026-03-14T11:47:52.123456Z")]
    public void TimesAreWrittenAtMicroseconds_WithoutTrailingZeros(string input, string expected)
    {
        var value = DateTime.Parse(input, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        Assert.Equal(expected, OpenAuditTime.Format(value));
    }
}
