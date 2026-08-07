using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Persistence;

/// <summary>What the provider said about a call that did not reach anybody.</summary>
// The call-log screen has always had a "Failure Reason" row; nothing ever wrote it, so a carrier
// rejecting every dial showed up as "Call Failed" and the cause was only in the provider's own panel.
internal static class VoiceCallFailureReason
{
    /// <summary>Keys a provider reports a cause under. Everything else in the payload is not a reason.</summary>
    private static readonly string[] ReasonKeys = ["code", "cause", "reason", "error"];

    /// <summary>Recorded when a call failed and the provider said nothing about why.</summary>
    internal const string NotReported = "The voice provider gave no reason.";

    /// <summary>The provider's words, or null when the status speaks for itself.</summary>
    public static string? Describe(CallStatus status, Func<string, string?> reported)
    {
        var parts = ReasonKeys
            .Select(reported)
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (parts.Length == 0)
            return status == CallStatus.Failed ? NotReported : null;

        var text = string.Join(' ', parts);
        return text.Length <= CallLog.MaxFailureReasonLength
            ? text
            : text[..(CallLog.MaxFailureReasonLength - 1)] + "…";
    }

    /// <summary>The reason as a clause to hang off a timeline sentence, or nothing.</summary>
    public static string Clause(string? reason) =>
        string.IsNullOrEmpty(reason) ? string.Empty : $" — {reason}";
}
