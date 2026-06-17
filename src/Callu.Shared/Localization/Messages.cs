using System.Reflection;
using System.Text.Json;

namespace Callu.Shared.Localization;

/// <summary>
/// Centralized message resolver that loads locale strings from an embedded JSON resource.
/// Usage: Messages.Get("auth.loginSuccess") → "Login successful"
/// </summary>
public static class Messages
{
    private static readonly Dictionary<string, string> _messages = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>
    /// Initialize messages from a JSON file path.
    /// Call once during application startup.
    /// </summary>
    public static void Initialize(string jsonFilePath)
    {
        lock (_lock)
        {
            if (_initialized) return;

            var json = File.ReadAllText(jsonFilePath);
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            FlattenJson(root, "", _messages);
            _initialized = true;
        }
    }

    /// <summary>
    /// Initialize messages from a JSON string directly.
    /// </summary>
    public static void InitializeFromJson(string json)
    {
        lock (_lock)
        {
            if (_initialized) return;

            var root = JsonSerializer.Deserialize<JsonElement>(json);
            FlattenJson(root, "", _messages);
            _initialized = true;
        }
    }

    /// <summary>
    /// Get a message by its dot-notation key.
    /// Returns the key itself if not found (fail-safe).
    /// </summary>
    public static string Get(string key)
    {
        if (_messages.TryGetValue(key, out var value))
            return value;

        return key;
    }

    /// <summary>
    /// Get a message and replace placeholders like {name} with values.
    /// </summary>
    public static string Get(string key, params (string Name, object Value)[] replacements)
    {
        var template = Get(key);
        foreach (var (name, value) in replacements)
        {
            template = template.Replace($"{{{name}}}", value?.ToString() ?? "");
        }
        return template;
    }

    /// <summary>
    /// Reset for testing purposes.
    /// </summary>
    internal static void Reset()
    {
        lock (_lock)
        {
            _messages.Clear();
            _initialized = false;
        }
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
