namespace Callu.Shared.Localization;

/// <summary>The languages this installation can write and speak in.</summary>
// One list, so a culture the validator accepts is always one the caller can actually be spoken to in.
public static class SupportedCultures
{
    public const string Fallback = "en-US";

    /// <summary>Culture code to the TTS resource that speaks it.</summary>
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en-US"] = "en-US",
        ["tr-TR"] = "tr-TR",
    };

    public static IReadOnlyCollection<string> All => Known.Keys;

    public static bool IsSupported(string? culture) =>
        !string.IsNullOrWhiteSpace(culture) && Known.ContainsKey(culture);

    /// <summary>The first culture in the chain this installation can speak, or the fallback.</summary>
    public static string Resolve(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (IsSupported(candidate)) return Known[candidate!];

            // "tr" and "tr-CY" both mean the Turkish resource; a two-letter UI code is what the
            // browser language switcher stores.
            var match = MatchByLanguage(candidate);
            if (match is not null) return match;
        }

        return Fallback;
    }

    private static string? MatchByLanguage(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        var language = candidate.Split('-')[0];
        if (language.Length == 0) return null;

        foreach (var known in Known.Keys)
        {
            if (known.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))
                return Known[known];
        }

        return null;
    }
}
