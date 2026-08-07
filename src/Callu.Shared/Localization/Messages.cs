using System.Globalization;
using System.Text.Json;

namespace Callu.Shared.Localization;

/// <summary>
/// Centralized message resolver. Usage: Messages.Get("auth.loginSuccess") → "Login successful".
/// The language comes from <see cref="CultureInfo.CurrentUICulture"/>, so call sites take no locale.
/// </summary>
public static class Messages
{
    public const string FallbackLanguage = "en";

    private static readonly object _lock = new();

    private static Dictionary<string, Dictionary<string, string>> _locales =
        new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> _fallback = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every language a locale file was found for, lowest-priority fallback first.</summary>
    public static IReadOnlyCollection<string> AvailableLanguages
    {
        get
        {
            lock (_lock) return [.. _locales.Keys];
        }
    }

    /// <summary>
    /// Loads every <c>*.json</c> in the directory; the file name is the language code (<c>tr.json</c> → <c>tr</c>).
    /// </summary>
    public static void Initialize(string localesDirectory)
    {
        if (!Directory.Exists(localesDirectory))
            throw new DirectoryNotFoundException(
                $"Locale directory '{localesDirectory}' does not exist. Without it every message would " +
                "resolve to its own key, so this fails at startup rather than in an operator's face.");

        var loaded = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(localesDirectory, "*.json"))
        {
            var language = Path.GetFileNameWithoutExtension(file);
            var messages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            FlattenJson(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file)), "", messages);

            if (messages.Count > 0) loaded[language] = messages;
        }

        Publish(loaded);
    }

    /// <summary>Loads one language from a JSON string, for tests and for a host with no locale directory.</summary>
    public static void InitializeFromJson(string json, string language = FallbackLanguage)
    {
        var messages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        FlattenJson(JsonSerializer.Deserialize<JsonElement>(json), "", messages);

        lock (_lock)
        {
            var loaded = new Dictionary<string, Dictionary<string, string>>(_locales, StringComparer.OrdinalIgnoreCase)
            {
                [language] = messages,
            };
            Publish(loaded);
        }
    }

    /// <summary>
    /// The message for the current UI culture, falling back to English and then to the key itself.
    /// </summary>
    public static string Get(string key)
    {
        var (locales, fallback) = Snapshot();

        if (Lookup(locales, CultureInfo.CurrentUICulture, key) is { } localized)
            return localized;

        return fallback.TryGetValue(key, out var english) ? english : key;
    }

    /// <summary>The message in a named language rather than the current culture's.</summary>
    // For text whose language is chosen by what is being described rather than by who is reading:
    // a sample spoken in the language of the template being previewed, not of the operator's panel.
    public static string GetIn(string language, string key)
    {
        var (locales, fallback) = Snapshot();

        if (!string.IsNullOrWhiteSpace(language))
        {
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(language); }
            catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }

            if (Lookup(locales, culture, key) is { } localized)
                return localized;
        }

        return fallback.TryGetValue(key, out var english) ? english : key;
    }

    /// <summary>Get a message and replace placeholders like {name} with values.</summary>
    public static string Get(string key, params (string Name, object Value)[] replacements)
    {
        var template = Get(key);
        foreach (var (name, value) in replacements)
        {
            template = template.Replace($"{{{name}}}", value?.ToString() ?? "");
        }
        return template;
    }

    internal static void Reset()
    {
        lock (_lock)
        {
            _locales = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            _fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Exact culture first (<c>pt-BR</c>), then its language (<c>pt</c>). Null when neither is loaded.</summary>
    private static string? Lookup(
        Dictionary<string, Dictionary<string, string>> locales,
        CultureInfo culture,
        string key)
    {
        foreach (var candidate in new[] { culture.Name, culture.TwoLetterISOLanguageName })
        {
            if (!string.IsNullOrEmpty(candidate)
                && locales.TryGetValue(candidate, out var messages)
                && messages.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static void Publish(Dictionary<string, Dictionary<string, string>> loaded)
    {
        lock (_lock)
        {
            _locales = loaded;
            // No en.json is a broken deployment, but a random language as the fallback would be worse
            // than a predictable one: order so the same file wins every start.
            _fallback = loaded.TryGetValue(FallbackLanguage, out var english)
                ? english
                : loaded.OrderBy(locale => locale.Key, StringComparer.Ordinal)
                        .Select(locale => locale.Value)
                        .FirstOrDefault() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static (Dictionary<string, Dictionary<string, string>> Locales, Dictionary<string, string> Fallback) Snapshot()
    {
        lock (_lock) return (_locales, _fallback);
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    FlattenJson(property.Value, key, output);
                }
                break;

            case JsonValueKind.String:
                output[prefix] = element.GetString() ?? "";
                break;
        }
    }
}
