using System.Text.Json;

namespace Callu.Shared.Localization;

/// <summary>
/// Loads TTS default message templates from JSON files under Resources/TtsDefaults/.
/// Each file is named by language code (e.g., en-US.json, tr-TR.json).
/// </summary>
public static class TtsDefaults
{
    private static readonly Dictionary<string, Dictionary<string, string>> _defaults = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _englishFallback = new();

    /// <summary>
    /// All known TTS message keys with human-readable descriptions and grouping.
    /// Used by the frontend to render the template editor dynamically.
    /// </summary>
    public static readonly List<TtsKeyDescriptor> AllKeys =
    [
        new("incident_message", "Incident Alert Message", "call_flow",
            "Main message played when the call connects. Variables: {service}, {severity_text}, {title}, {description}"),

        new("severity_critical", "Severity — Critical", "call_flow",
            "The word {severity_text} is replaced with, in the language the call is spoken in"),
        new("severity_high", "Severity — High", "call_flow",
            "The word {severity_text} is replaced with, in the language the call is spoken in"),
        new("severity_medium", "Severity — Medium", "call_flow",
            "The word {severity_text} is replaced with, in the language the call is spoken in"),
        new("severity_low", "Severity — Low", "call_flow",
            "The word {severity_text} is replaced with, in the language the call is spoken in"),
        new("dtmf_prompt", "DTMF Key Prompt", "call_flow",
            "Instructions for keypad actions (acknowledge, escalate, repeat, AI, conference)"),
        new("ack_confirm", "Acknowledge Confirmation", "call_flow",
            "Played after the user presses 1 to acknowledge"),
        new("escalation_confirm", "Escalation Confirmation", "call_flow",
            "Played after the user presses 2 to escalate"),
        new("ack_failed", "Acknowledge — Not Registered", "call_flow",
            "Played when the acknowledgement could not be delivered to Callu, so the incident is still open"),
        new("escalation_failed", "Escalation — Not Registered", "call_flow",
            "Played when the escalation request could not be delivered to Callu, so the incident is still open"),
        new("invalid_key", "Invalid Key", "call_flow",
            "Played when an unrecognized key is pressed"),

        new("conference_wait", "Conference — Please Wait", "conference",
            "Played while setting up the video conference"),
        new("conference_success", "Conference — Ready", "conference",
            "Played when the conference is created. Variables: {count}"),
        new("conference_fail", "Conference — Failed", "conference",
            "Played when conference creation fails"),
        new("conference_duplicate", "Conference — Already Requested", "conference",
            "Played when a conference was already requested for this incident"),
        new("conference_requested", "Conference — Request Recorded", "conference",
            "Played by a provider that records the request and ends the call rather than bridging anyone in")
    ];

    /// <summary>How much of a description a call reads before the keypad options.</summary>
    // It is free text from whatever raised the alert — a paragraph, a stack trace, a URL — and every
    // character is read out before the person hears how to acknowledge.
    public const int MaxSpokenDescriptionLength = 200;

    /// <summary>The severity in the language the call is spoken in, or the raw value if it has no word.</summary>
    public static string SeverityWord(IReadOnlyDictionary<string, string> messages, string? severity)
    {
        var raw = string.IsNullOrWhiteSpace(severity) ? "Medium" : severity.Trim();
        return messages.TryGetValue($"severity_{raw.ToLowerInvariant()}", out var word)
               && !string.IsNullOrWhiteSpace(word)
            ? word
            : raw;
    }

    /// <summary>The part of a description worth reading out loud.</summary>
    public static string ClipDescription(string? description)
    {
        var text = description?.Trim();
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return text.Length <= MaxSpokenDescriptionLength
            ? text
            : text[..MaxSpokenDescriptionLength].TrimEnd() + "…";
    }

    /// <summary>
    /// Load all TTS default JSON files from the given directory.
    /// Call this at startup (e.g., in Program.cs).
    /// </summary>
    /// <returns>One entry per language file that could not be loaded; empty when all of them loaded.</returns>
    public static IReadOnlyList<TtsDefaultsLoadFailure> Initialize(string ttsDefaultsDir)
    {
        // A missing file is not thrown on: a language nobody configured must not stop the host. The
        // caller logs what did not load, because the only other symptom is the wrong language on a call.
        if (!Directory.Exists(ttsDefaultsDir))
            return [new TtsDefaultsLoadFailure(ttsDefaultsDir, "The TTS defaults directory does not exist.")];

        var failures = new List<TtsDefaultsLoadFailure>();

        foreach (var file in Directory.GetFiles(ttsDefaultsDir, "*.json"))
        {
            var langCode = Path.GetFileNameWithoutExtension(file);
            try
            {
                var json = File.ReadAllText(file);
                var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (messages != null)
                    _defaults[langCode] = messages;
                else
                    failures.Add(new TtsDefaultsLoadFailure(langCode, "The file parsed to null."));
            }
            catch (Exception ex)
            {
                failures.Add(new TtsDefaultsLoadFailure(langCode, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        if (_defaults.TryGetValue("en-US", out var en))
            _englishFallback = en;

        return failures;
    }

    /// <summary>
    /// Gets the default messages for a specific language.
    /// Falls back to English if the language doesn't have defaults.
    /// </summary>
    public static Dictionary<string, string> GetDefaults(string languageCode)
    {
        if (_defaults.TryGetValue(languageCode, out var langDefaults))
            return new Dictionary<string, string>(langDefaults);

        return new Dictionary<string, string>(_englishFallback);
    }

    /// <summary>
    /// Gets the English default messages (backward compatibility).
    /// </summary>
    public static Dictionary<string, string> GetEnglishDefaults() => new(_englishFallback);

    /// <summary>
    /// Returns all available default language codes.
    /// </summary>
    public static List<string> GetAvailableLanguages() => [.. _defaults.Keys];
}

/// <summary>
/// Describes a TTS message key for the UI template editor.
/// </summary>
public record TtsKeyDescriptor(string Key, string Label, string Group, string Description);

/// <summary>A language whose spoken defaults could not be loaded, and why.</summary>
public record TtsDefaultsLoadFailure(string LanguageCode, string Reason);
