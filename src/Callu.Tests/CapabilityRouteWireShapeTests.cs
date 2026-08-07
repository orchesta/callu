using System.Text.Json;
using System.Text.Json.Serialization;
using Callu.Domain.Enums;
using Callu.Shared.Models.Communication;

namespace Callu.Tests;

/// <summary>The shape the routing screen actually receives, serialized the way the API serializes it.</summary>
// The screen reads the capability to pick a channel name. It shipped keyed by the enum's numbers
// while the API sends its names, so every row was labelled "Channel" and the tests, which built
// their own numeric fixtures, stayed green.
public class CapabilityRouteWireShapeTests
{
    // Program.cs adds exactly this converter to the MVC JSON options.
    private static readonly JsonSerializerOptions ApiOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void TheCapabilityGoesOverTheWireAsItsName_NotItsNumber()
    {
        var json = JsonSerializer.Serialize(
            new CapabilityRouteDto { Capability = CommunicationCapability.VoiceCalls }, ApiOptions);

        Assert.Contains("\"capability\":\"VoiceCalls\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"capability\":1", json, StringComparison.Ordinal);
    }

    /// <summary>Every routable channel's name, so the screen's map can be checked against this list.</summary>
    [Theory]
    [InlineData(CommunicationCapability.VoiceCalls, "VoiceCalls")]
    [InlineData(CommunicationCapability.Sms, "Sms")]
    [InlineData(CommunicationCapability.WhatsApp, "WhatsApp")]
    [InlineData(CommunicationCapability.VideoConference, "VideoConference")]
    public void EachRoutableChannelSerialisesToTheNameTheScreenLooksUp(
        CommunicationCapability capability, string expected)
    {
        var json = JsonSerializer.Serialize(new CapabilityRouteDto { Capability = capability }, ApiOptions);

        Assert.Contains($"\"capability\":\"{expected}\"", json, StringComparison.Ordinal);
    }
}
