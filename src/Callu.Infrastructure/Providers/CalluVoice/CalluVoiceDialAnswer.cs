using System.Net.Http;

namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>What callu-voice's answer to POST /calls says about whether a call went out.</summary>
public enum CalluVoiceDialAnswer
{
    /// <summary>The page is on its way, and its callbacks own the retry chain from here.</summary>
    Placed,

    /// <summary>A call with this id is already running, so a page is on its way even though this attempt did not place it.</summary>
    AlreadyInFlight,

    /// <summary>Nothing was dialled, and the service said so — the next attempt can follow soon.</summary>
    Refused,

    /// <summary>Nothing that says whether a call went out; re-dialling on this could double-ring the responder.</summary>
    Undetermined
}

/// <summary>Reads a callu-voice answer, or a transport failure, as one of <see cref="CalluVoiceDialAnswer"/>.</summary>
public static class CalluVoiceDialAnswers
{
    /// <summary>Which answer an HTTP status code from POST /calls carries.</summary>
    // 502 and 503 are documented as "nothing was dialled", and a 4xx is a refusal by definition. What is
    // left says nothing either way, and guessing "refused" there is what re-dials a call already placed.
    public static CalluVoiceDialAnswer ForStatusCode(int statusCode) => statusCode switch
    {
        202 => CalluVoiceDialAnswer.Placed,
        409 => CalluVoiceDialAnswer.AlreadyInFlight,
        502 or 503 => CalluVoiceDialAnswer.Refused,
        >= 400 and < 500 => CalluVoiceDialAnswer.Refused,
        _ => CalluVoiceDialAnswer.Undetermined
    };

    /// <summary>Whether a transport failure happened before any request could reach callu-voice.</summary>
    // All four fail while the connection is still being made, so no call can have been placed. Every
    // other transport failure can happen with the request already in the service's hands.
    public static bool NeverReachedTheService(HttpRequestError error) => error is
        HttpRequestError.NameResolutionError or
        HttpRequestError.ConnectionError or
        HttpRequestError.SecureConnectionError or
        HttpRequestError.ProxyTunnelError;
}
