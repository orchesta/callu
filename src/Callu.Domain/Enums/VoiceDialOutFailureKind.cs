namespace Callu.Domain.Enums;

/// <summary>Why a voice retry could not get a call out of the building — three distinct facts, bounded and reported differently.</summary>
public enum VoiceDialOutFailureKind
{
    /// <summary>
    /// No voice provider in the registry. Nobody was asked, so nothing can be concluded about a
    /// provider — and a chain must never be given up on over a question that was never put to one.
    /// </summary>
    ProviderMissing = 0,

    /// <summary>The provider answered, and the answer was "I did not place this call".</summary>
    ProviderRefused = 1,

    /// <summary>
    /// The provider threw. An HTTP timeout arrives here and says nothing about what the far end did:
    /// the call may or may not be ringing the responder right now.
    /// </summary>
    ProviderThrew = 2
}
