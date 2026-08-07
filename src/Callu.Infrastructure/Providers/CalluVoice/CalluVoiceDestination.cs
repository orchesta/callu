using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>A stored phone number after the adapter has tried to put it in E.164 form.</summary>
public readonly record struct CalluVoiceDestinationResult(string? Number, string? Refusal)
{
    [MemberNotNullWhen(true, nameof(Number))]
    public bool IsUsable => Number is not null;
}

/// <summary>Turns a stored phone number into the E.164 form callu-voice dials, or refuses it.</summary>
public static partial class CalluVoiceDestination
{
    /// <summary>The only destination shape callu-voice accepts.</summary>
    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164();

    private const string Example = "+905321234567";

    /// <summary>Strips formatting, turns a leading international access code into "+", and refuses
    /// anything that would need a country code invented for it.</summary>
    public static CalluVoiceDestinationResult Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Refuse(raw, "it is empty");

        var compact = Compact(raw);

        // "00" is the international access code, so replacing it with "+" adds nothing that was not
        // already said. Every other national prefix would mean inventing a country code.
        if (compact.StartsWith("00", StringComparison.Ordinal) && compact.Length > 2)
            compact = string.Concat("+", compact.AsSpan(2));

        if (E164().IsMatch(compact))
            return new CalluVoiceDestinationResult(compact, null);

        return Refuse(raw, Reason(compact));
    }

    private static string Compact(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (c is '-' or '(' or ')' or '.' or '/' or '‐' or '‑' or '–' or '—') continue;
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static string Reason(string compact)
    {
        if (compact.Length == 0)
            return "it has no digits in it";

        if (!compact.StartsWith('+'))
        {
            return compact.StartsWith('0')
                ? "it starts with a national trunk prefix, and the country code it belongs to cannot be worked out from the number alone"
                : "it has no leading '+', and the digits alone do not say which country they belong to";
        }

        if (compact.AsSpan(1).ContainsAnyExcept("0123456789"))
            return "it contains characters that are not digits";

        if (compact.StartsWith("+0", StringComparison.Ordinal))
            return "no country code begins with 0";

        return $"an E.164 number carries 7 to 15 digits and this one carries {compact.Length - 1}";
    }

    private static CalluVoiceDestinationResult Refuse(string? raw, string reason) =>
        new(null, $"Cannot place a voice call to '{raw}': {reason}. callu-voice dials only "
                  + $"international E.164 numbers such as {Example}, and nothing was dialled. "
                  + "Correct the phone number on the responder's profile — until it is corrected "
                  + "this person cannot be reached by phone.");
}
