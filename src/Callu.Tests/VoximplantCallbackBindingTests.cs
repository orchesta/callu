using Callu.Api.Controllers;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Voximplant;

namespace Callu.Tests;

/// <summary>Pins the token-exempt callback statuses to the ones the persistence layer actually drops.</summary>
public class VoximplantCallbackBindingTests
{
    private static readonly string[] AllVoxStatuses =
    [
        "alerting",
        "connected",
        "acknowledged",
        "escalated",
        "failed",
        "no_answer",
        "voicemail",
        "timeout",
        "conference_created",
        "conference_started",
        "conference_ended",
        "participant_joined",
        "participant_left",
        "something-unexpected",
    ];

    [Fact]
    public void TokenExemptStatuses_AreExactly_TheStatusesPersistenceDrops()
    {
        foreach (var status in AllVoxStatuses)
        {
            Assert.Equal(
                VoximplantVoiceCallbackPersistence.IsConferenceLifecycleStatus(status),
                VoximplantCallbackController.InertConferenceLifecycleStatuses.Contains(status));
        }
    }

    /// <summary>
    /// The statuses that silence an escalation are the whole point of the binding — none of them
    /// may be reachable without a token.
    /// </summary>
    [Theory]
    [InlineData("acknowledged")]
    [InlineData("escalated")]
    [InlineData("conference_created")]
    public void StateChangingStatuses_AreNeverExemptFromTheCallToken(string status)
    {
        Assert.False(VoximplantCallbackController.InertConferenceLifecycleStatuses.Contains(status));
        Assert.False(VoximplantVoiceCallbackPersistence.IsConferenceLifecycleStatus(status));
    }

    /// <summary>
    /// An unknown status maps to Connected and would write a call log, so it must not be exempt —
    /// otherwise "status: anything" is a free-form write against a forged incident id.
    /// </summary>
    [Fact]
    public void UnknownStatus_IsNotExempt()
    {
        Assert.Equal(CallStatus.Connected, VoximplantVoiceCallbackPersistence.MapVoxStatus("whatever"));
        Assert.False(VoximplantCallbackController.InertConferenceLifecycleStatuses.Contains("whatever"));
    }

    [Fact]
    public void ExemptStatuses_AreMatchedCaseInsensitively() =>
        Assert.True(VoximplantCallbackController.InertConferenceLifecycleStatuses.Contains("Participant_Joined"));
}
