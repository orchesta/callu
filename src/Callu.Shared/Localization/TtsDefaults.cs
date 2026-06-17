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
        new("dtmf_prompt", "DTMF Key Prompt", "call_flow",
            "Instructions for keypad actions (acknowledge, escalate, repeat, AI, conference)"),
        new("ack_confirm", "Acknowledge Confirmation", "call_flow",
            "Played after the user presses 1 to acknowledge"),
        new("escalation_confirm", "Escalation Confirmation", "call_flow",
            "Played after the user presses 2 to escalate"),
        new("invalid_key", "Invalid Key", "call_flow",
            "Played when an unrecognized key is pressed"),

        new("conference_wait", "Conference — Please Wait", "conference",
            "Played while setting up the video conference"),
        new("conference_success", "Conference — Ready", "conference",
            "Played when the conference is created. Variables: {count}"),
        new("conference_fail", "Conference — Failed", "conference",
            "Played when conference creation fails"),
        new("conference_duplicate", "Conference — Already Requested", "conference",
            "Played when a conference was already requested for this incident")
    ];

    /// <summary>
    /// Load all TTS default JSON files from the given directory.
    /// Call this at startup (e.g., in Program.cs).
    /// </summary>
    public static void Initialize(string ttsDefaultsDir)
    {
        if (!Directory.Exists(ttsDefaultsDir)) return;

        foreach (var file in Directory.GetFiles(ttsDefaultsDir, "*.json"))
        {
            var langCode = Path.GetFileNameWithoutExtension(file);
            try
            {
                var json = File.ReadAllText(file);
                var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (messages != null)
                {
                    _defaults[langCode] = messages;
                }
            }
            catch (Exception)
            {
            }
        }

        if (_defaults.TryGetValue("en-US", out var en))
            _englishFallback = en;
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
