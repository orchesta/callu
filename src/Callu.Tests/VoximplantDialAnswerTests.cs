using System.Net.Http;
using Callu.Infrastructure.Providers.Voximplant;

namespace Callu.Tests;

/// <summary>
/// Whether a Voximplant answer means "nothing was dialled" or "cannot tell", which is what decides
/// between a prompt retry and leaving the call log's own chain to own the attempt.
/// </summary>
// The sibling provider was built this way first; the two have to agree or one of them re-dials a
// responder the other would leave alone.
public class VoximplantDialAnswerTests
{
    [Theory]
    [InlineData(200, VoximplantDialAnswer.Placed)]
    [InlineData(204, VoximplantDialAnswer.Placed)]
    [InlineData(400, VoximplantDialAnswer.Refused)]
    [InlineData(401, VoximplantDialAnswer.Refused)]
    [InlineData(429, VoximplantDialAnswer.Refused)]
    [InlineData(502, VoximplantDialAnswer.Refused)]
    [InlineData(503, VoximplantDialAnswer.Refused)]
    public void AnAnswerThatSettlesTheQuestion_IsNotUndetermined(int status, VoximplantDialAnswer expected) =>
        Assert.Equal(expected, VoximplantDialAnswers.ForStatusCode(status));

    /// <summary>A gateway that timed out after the request landed may well have started the scenario.</summary>
    [Theory]
    [InlineData(500)]
    [InlineData(504)]
    [InlineData(599)]
    public void AnAnswerThatCouldLeaveAScenarioRunning_IsUndetermined(int status) =>
        Assert.Equal(VoximplantDialAnswer.Undetermined, VoximplantDialAnswers.ForStatusCode(status));

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    [InlineData(HttpRequestError.ProxyTunnelError)]
    public void ATransportFailureBeforeTheRequestLanded_MeansNothingWasDialled(HttpRequestError error) =>
        Assert.True(VoximplantDialAnswers.NeverReachedTheService(error));

    /// <summary>A failure mid-exchange says nothing, so it must not be read as "no call was placed".</summary>
    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.Unknown)]
    [InlineData(HttpRequestError.InvalidResponse)]
    public void ATransportFailureAfterTheRequestLanded_DoesNotClaimNothingWasDialled(HttpRequestError error) =>
        Assert.False(VoximplantDialAnswers.NeverReachedTheService(error));

    /// <summary>The two providers must classify the shared cases the same way.</summary>
    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    [InlineData(HttpRequestError.ProxyTunnelError)]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.Unknown)]
    public void BothProvidersAgreeOnWhichTransportFailuresNeverReachedTheService(HttpRequestError error) =>
        Assert.Equal(
            Callu.Infrastructure.Providers.CalluVoice.CalluVoiceDialAnswers.NeverReachedTheService(error),
            VoximplantDialAnswers.NeverReachedTheService(error));
}
