using System.Globalization;
using System.Text.Json;
using Callu.Shared.Localization;

namespace Callu.Tests;

/// <summary>The locale files the API ships, and how a request's language picks one.</summary>
public class MessagesLocalizationTests
{
    private static string LocalesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CalluApp.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        var locales = Path.Combine(directory!.FullName, "Callu.Api", "Resources", "Locales");
        Assert.True(Directory.Exists(locales), $"Expected the locale files under {locales}");

        return locales;
    }

    private static Dictionary<string, string> Flatten(string file)
    {
        var flat = new Dictionary<string, string>(StringComparer.Ordinal);

        void Walk(JsonElement element, string prefix)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}");
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                flat[prefix] = element.GetString() ?? "";
            }
        }

        Walk(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file)), "");
        return flat;
    }

    private static string Localized(string language, string key)
    {
        Messages.Initialize(LocalesDirectory());

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            return Messages.Get(key);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void EveryLocaleFile_CarriesTheSameKeys()
    {
        var files = Directory.GetFiles(LocalesDirectory(), "*.json");

        Assert.True(files.Length >= 2, "Expected at least two locale files; parity is what this test is about.");

        var byLanguage = files.ToDictionary(f => Path.GetFileNameWithoutExtension(f)!, Flatten);
        var english = byLanguage["en"];

        foreach (var (language, messages) in byLanguage.Where(l => l.Key != "en"))
        {
            var missing = english.Keys.Except(messages.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var extra = messages.Keys.Except(english.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.True(missing.Count == 0,
                $"{language}.json is missing: {string.Join(", ", missing)}.\n\n"
                + "A missing key is not a missing translation — Messages.Get falls back to English, but a key "
                + "present in neither reads back as the key itself, so the operator sees `auth.lockedOut`.");

            Assert.True(extra.Count == 0,
                $"{language}.json has keys en.json does not: {string.Join(", ", extra)}. "
                + "Either the English side was forgotten or the key is dead.");
        }
    }

    [Fact]
    public void NoLocaleFile_HasABlankTranslation()
    {
        var blank = Directory.GetFiles(LocalesDirectory(), "*.json")
            .SelectMany(f => Flatten(f).Where(m => string.IsNullOrWhiteSpace(m.Value))
                .Select(m => $"{Path.GetFileName(f)}:{m.Key}"))
            .ToList();

        Assert.True(blank.Count == 0, "These entries are blank: " + string.Join(", ", blank));
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("tr-TR")]
    public void ATurkishRequest_GetsTheTurkishMessage(string language)
    {
        var turkish = Flatten(Path.Combine(LocalesDirectory(), "tr.json"));

        Assert.Equal(turkish["auth.invalidCredentials"], Localized(language, "auth.invalidCredentials"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-GB")]
    [InlineData("de")]
    [InlineData("")]
    public void EverythingElse_FallsBackToEnglish(string language)
    {
        var english = Flatten(Path.Combine(LocalesDirectory(), "en.json"));

        Assert.Equal(english["auth.invalidCredentials"], Localized(language, "auth.invalidCredentials"));
    }

    [Fact]
    public void AnUnknownKey_StillReadsBackAsItself()
    {
        Assert.Equal("nope.not.a.key", Localized("tr", "nope.not.a.key"));
    }

    [Fact]
    public void TheHostsAdvertise_EveryLanguageThatWasLoaded()
    {
        Messages.Initialize(LocalesDirectory());

        var expected = Directory.GetFiles(LocalesDirectory(), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(l => l, StringComparer.Ordinal);

        Assert.Equal(expected, Messages.AvailableLanguages.OrderBy(l => l, StringComparer.Ordinal));
    }

    [Fact]
    public void AMissingLocaleDirectory_FailsAtStartupRatherThanSilently()
    {
        var missing = Path.Combine(Path.GetTempPath(), "callu-locales-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(() => Messages.Initialize(missing));

        Messages.Initialize(LocalesDirectory());
    }
}
