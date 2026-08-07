using System.Text.Json;
using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>Every placeholder the shipped call templates use has to be substituted by every provider.</summary>
// A placeholder nobody replaces is not an error anywhere: the call simply reads the braces out loud to
// whoever answered. The Voximplant path shipped for months without substituting {severity_text}, and
// nothing but listening to a real call would have shown it.
public class SpokenPlaceholderGuardTests
{
    private static readonly string[] SubstitutingFiles =
    [
        "Callu.Infrastructure/Providers/CalluVoice/CalluVoiceRequestBuilder.cs",
        "Callu.Infrastructure/Providers/Voximplant/VoximplantProvider.cs",
    ];

    /// <summary>Placeholders filled from elsewhere than the incident, so the call providers never see them.</summary>
    private static readonly HashSet<string> FilledDownstream = new(StringComparer.Ordinal) { "count" };

    private static IEnumerable<string> ShippedTemplateFiles() =>
        Directory.GetFiles(Path.Combine(SourceScanner.Root().FullName, "Callu.Api", "Resources", "TtsDefaults"), "*.json");

    private static HashSet<string> PlaceholdersIn(string json)
    {
        var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(json))!;
        return [.. messages.Values
            .SelectMany(v => Regex.Matches(v, @"\{([a-z_]+)\}").Select(m => m.Groups[1].Value))
            .Where(name => !FilledDownstream.Contains(name))];
    }

    [Fact]
    public void EveryPlaceholderTheTemplatesUse_IsSubstitutedByEveryProvider()
    {
        var placeholders = ShippedTemplateFiles().SelectMany(PlaceholdersIn).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(placeholders);

        var unsubstituted = new List<string>();
        foreach (var relative in SubstitutingFiles)
        {
            var source = File.ReadAllText(Path.Combine(SourceScanner.Root().FullName, relative));
            unsubstituted.AddRange(placeholders
                .Where(p => !source.Contains($"\"{p}\"", StringComparison.Ordinal)
                            && !source.Contains($"{{{p}}}", StringComparison.Ordinal))
                .Select(p => $"{Path.GetFileName(relative)} never substitutes {{{p}}}"));
        }

        Assert.Empty(unsubstituted);
    }

    /// <summary>Both languages offer the same placeholders, so switching language cannot lose a value.</summary>
    [Fact]
    public void EveryShippedLanguage_UsesTheSamePlaceholders()
    {
        var byFile = ShippedTemplateFiles().ToDictionary(f => Path.GetFileNameWithoutExtension(f)!, PlaceholdersIn);
        var union = byFile.Values.SelectMany(p => p).ToHashSet(StringComparer.Ordinal);

        var missing = byFile
            .SelectMany(kvp => union.Except(kvp.Value).Select(p => $"{kvp.Key} never uses {{{p}}}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    /// <summary>Every message key exists in every shipped language.</summary>
    [Fact]
    public void EveryShippedLanguage_DefinesTheSameKeys()
    {
        var byFile = ShippedTemplateFiles().ToDictionary(
            f => Path.GetFileNameWithoutExtension(f)!,
            f => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!.Keys.ToHashSet(StringComparer.Ordinal));

        var union = byFile.Values.SelectMany(k => k).ToHashSet(StringComparer.Ordinal);

        var missing = byFile
            .SelectMany(kvp => union.Except(kvp.Value).Select(k => $"{kvp.Key} is missing {k}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }
}
