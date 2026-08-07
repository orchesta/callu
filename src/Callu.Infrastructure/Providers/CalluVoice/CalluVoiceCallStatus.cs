using Callu.Domain.Enums;

namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>What one callu-voice status says happened to a call.</summary>
public sealed record CalluVoiceCallOutcome(
    CallStatus Status,
    bool EndsTheCall,
    bool StandsDownRetryChain,
    bool AcknowledgesTheIncident = false);

/// <summary>The status vocabulary callu-voice reports, and what each one means for a call log.</summary>
public static class CalluVoiceCallStatus
{
    // A retry chain stands down only where a human demonstrably took the page. Ringing out, answering
    // and staying silent, and being cut off by a restart all mean nobody has it yet.
    // Asking to be brought in owns the incident the same way pressing acknowledge does; handing it on
    // does not, so escalating leaves the incident where it was.
    private static readonly Dictionary<string, CalluVoiceCallOutcome> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alerting"] = new(CallStatus.Initiated, EndsTheCall: false, StandsDownRetryChain: false),
        ["connected"] = new(CallStatus.Connected, EndsTheCall: false, StandsDownRetryChain: false),
        ["acknowledged"] = new(CallStatus.Acknowledged, EndsTheCall: true, StandsDownRetryChain: true,
            AcknowledgesTheIncident: true),
        ["escalated"] = new(CallStatus.Escalated, EndsTheCall: true, StandsDownRetryChain: true),
        ["conference_requested"] = new(CallStatus.ConferenceRequested, EndsTheCall: true, StandsDownRetryChain: true,
            AcknowledgesTheIncident: true),
        ["voicemail"] = new(CallStatus.Voicemail, EndsTheCall: true, StandsDownRetryChain: false),
        ["no_answer"] = new(CallStatus.NoAnswer, EndsTheCall: true, StandsDownRetryChain: false),
        ["silence_timeout"] = new(CallStatus.SilenceTimeout, EndsTheCall: true, StandsDownRetryChain: false),
        ["timeout"] = new(CallStatus.Timeout, EndsTheCall: true, StandsDownRetryChain: false),
        ["failed"] = new(CallStatus.Failed, EndsTheCall: true, StandsDownRetryChain: false)
    };

    /// <summary>Where a status this version does not know lands.</summary>
    // Failed rather than a healthy state: it closes the leg, leaves the retry chain armed, and shows up
    // on the incident timeline, so an unrecognized status keeps paging instead of quietly ending the page.
    public static readonly CalluVoiceCallOutcome Unrecognized =
        new(CallStatus.Failed, EndsTheCall: true, StandsDownRetryChain: false);

    /// <summary>The status names this table recognizes.</summary>
    public static IReadOnlyCollection<string> Known => Table.Keys;

    /// <summary>Maps a reported status, falling back to <see cref="Unrecognized"/>.</summary>
    public static CalluVoiceCallOutcome Map(string? status) =>
        status is not null && Table.TryGetValue(status.Trim(), out var outcome) ? outcome : Unrecognized;

    /// <summary>Whether the status is one this table knows, so an operator can be told when it is not.</summary>
    public static bool IsKnown(string? status) => status is not null && Table.ContainsKey(status.Trim());
}
