using System.Net.Http;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>What Voximplant's answer to StartScenarios says about whether a call went out.</summary>
public enum VoximplantDialAnswer
{
    /// <summary>The scenario started, and its callbacks own the retry chain from here.</summary>
    Placed,

    /// <summary>Nothing was started, and the platform said so — the next attempt can follow soon.</summary>
    Refused,

    /// <summary>Nothing that says whether a scenario started; re-dialling on this could double-ring the responder.</summary>
    Undetermined
}

/// <summary>Reads a Voximplant answer, or a transport failure, as one of <see cref="VoximplantDialAnswer"/>.</summary>
public static class VoximplantDialAnswers
{
    /// <summary>Which answer a non-2xx status from StartScenarios carries.</summary>
    // The platform reports both success and refusal as 200 with a body, so a non-2xx is a transport-level
    // fact. A 4xx is a refusal by definition and 502/503 are raised before the request reaches the
    // scenario engine. Everything else — 500, 504 — can leave a scenario already running.
    public static VoximplantDialAnswer ForStatusCode(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => VoximplantDialAnswer.Placed,
        >= 400 and < 500 => VoximplantDialAnswer.Refused,
        502 or 503 => VoximplantDialAnswer.Refused,
        _ => VoximplantDialAnswer.Undetermined
    };

    /// <summary>Whether a transport failure happened before any request could reach Voximplant.</summary>
    // All four fail while the connection is still being made, so no scenario can have been started.
    public static bool NeverReachedTheService(HttpRequestError error) => error is
        HttpRequestError.NameResolutionError or
        HttpRequestError.ConnectionError or
        HttpRequestError.SecureConnectionError or
        HttpRequestError.ProxyTunnelError;
}
