using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Voximplant;

namespace Callu.Tests;

/// <summary>
/// VoxEngine callbacks arrive as free-form status strings; MapVoxStatus normalizes them and
/// IsTerminalCallStatus decides whether a leg is done (drives retry/expiry). Locks both in.
/// </summary>
public class VoximplantCallStatusTests
{
    [Theory]
    [InlineData("alerting", CallStatus.Initiated)]
    [InlineData("connected", CallStatus.Connected)]
    [InlineData("acknowledged", CallStatus.Acknowledged)]
    [InlineData("escalated", CallStatus.Escalated)]
    [InlineData("failed", CallStatus.Failed)]
    [InlineData("no_answer", CallStatus.NoAnswer)]
    [InlineData("voicemail", CallStatus.Voicemail)]
    [InlineData("timeout", CallStatus.Timeout)]
    [InlineData("conference_created", CallStatus.ConferenceCreated)]
    public void MapVoxStatus_MapsKnownStatuses(string vox, CallStatus expected) =>
        Assert.Equal(expected, VoximplantVoiceCallbackPersistence.MapVoxStatus(vox));

    [Fact]
    public void MapVoxStatus_IsCaseInsensitive() =>
        Assert.Equal(CallStatus.Acknowledged, VoximplantVoiceCallbackPersistence.MapVoxStatus("ACKNOWLEDGED"));

    [Fact]
    public void MapVoxStatus_UnknownDefaultsToConnected() =>
        Assert.Equal(CallStatus.Connected, VoximplantVoiceCallbackPersistence.MapVoxStatus("something-unexpected"));

    [Theory]
    [InlineData(CallStatus.Acknowledged, true)]
    [InlineData(CallStatus.Escalated, true)]
    [InlineData(CallStatus.Failed, true)]
    [InlineData(CallStatus.NoAnswer, true)]
    [InlineData(CallStatus.Voicemail, true)]
    [InlineData(CallStatus.Timeout, true)]
    [InlineData(CallStatus.ConferenceCreated, true)]
    [InlineData(CallStatus.Initiated, false)]
    [InlineData(CallStatus.Connected, false)]
    public void IsTerminalCallStatus_MatchesSpec(CallStatus status, bool terminal) =>
        Assert.Equal(terminal, VoximplantVoiceCallbackPersistence.IsTerminalCallStatus(status));
}
