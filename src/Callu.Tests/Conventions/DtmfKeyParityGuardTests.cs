using System.Text.RegularExpressions;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Localization;

namespace Callu.Tests.Conventions;

/// <summary>
/// A key the product speaks must be a key the product answers. The two live apart — the menu is in the
/// shipped TTS templates, the scenario's own fallback messages and its header, while the handlers are
/// the scenario's switch and the self-hosted adapter's key constants — so changing one key means five
/// hand edits. A menu offering a key nothing handles is a responder pressing it and being told it is
/// invalid; a handler on a key nobody announces is a key never pressed.
/// </summary>
public class DtmfKeyParityGuardTests
{
    /// <summary>Digits only: a digit is spoken as itself in every language, while * and # are words.</summary>
    private static readonly Regex SpokenDigits = new(@"\d+", RegexOptions.Compiled);

    /// <summary>A single-key branch in the scenario: <c>case "1":</c> or <c>e.tone === "9"</c>.</summary>
    private static readonly Regex ScenarioKey = new(
        @"case\s*""(?<key>[0-9*#])""\s*:|e\.tone\s*===\s*""(?<key>[0-9*#])""",
        RegexOptions.Compiled);

    private static readonly Regex FallbackPrompt = new(
        @"""dtmf_prompt""\s*:\s*""(?<text>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled);

    /// <summary>The scenario's header line, which documents the key map for whoever edits it next.</summary>
    private static readonly Regex HeaderKeyMap = new(@"DTMF:(?<map>[^\r\n]*)", RegexOptions.Compiled);

    private static string IncidentCallScript()
    {
        var assembly = typeof(VoximplantProviderLifecycle).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(".Scripts.callu-incident-call.js", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<string> Digits(string text) =>
        [.. SpokenDigits.Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The digit keys the self-hosted adapter binds in the body it sends.</summary>
    private static List<string> AdapterDigitKeys() =>
    [
        .. new[]
            {
                CalluVoiceRequestBuilder.AcknowledgeKey,
                CalluVoiceRequestBuilder.EscalateKey,
                CalluVoiceRequestBuilder.RepeatKey,
                CalluVoiceRequestBuilder.ConferenceKey
            }
            .Where(k => k.All(char.IsAsciiDigit))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
    ];

    /// <summary>The digit keys the VoxEngine scenario actually branches on.</summary>
    private static List<string> ScenarioDigitKeys()
    {
        var branches = ScenarioKey.Matches(IncidentCallScript())
            .Select(m => m.Groups["key"].Value)
            .Where(k => k.All(char.IsAsciiDigit))
            .ToList();

        Assert.True(branches.Count >= 2,
            $"only {branches.Count} digit DTMF branches were found in the incident scenario; the shape this "
            + "guard reads has changed and it has stopped guarding anything.");

        return [.. branches.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>Every place the product reads the key menu aloud, labelled so a failure says which one drifted.</summary>
    public static TheoryData<string, string> SpokenMenus()
    {
        TtsDefaults.Initialize(Path.Combine(AppContext.BaseDirectory, "Resources", "TtsDefaults"));

        var script = IncidentCallScript();
        var menus = new TheoryData<string, string>();

        foreach (var language in TtsDefaults.GetAvailableLanguages())
        {
            if (TtsDefaults.GetDefaults(language).TryGetValue("dtmf_prompt", out var prompt)
                && !string.IsNullOrWhiteSpace(prompt))
                menus.Add($"the shipped {language} key menu", prompt);
        }

        var fallback = FallbackPrompt.Match(script);
        if (fallback.Success)
            menus.Add("the incident scenario's own fallback key menu", fallback.Groups["text"].Value);

        var header = HeaderKeyMap.Match(script);
        if (header.Success)
            menus.Add("the incident scenario's header key map", header.Groups["map"].Value);

        menus.Add("the adapter's stand-in menu, spoken when no template resolves",
            CalluVoiceRequestBuilder.NoTemplatePrompt);

        return menus;
    }

    /// <summary>
    /// THE guard: every spoken menu names exactly the digit keys that are answered. A menu still saying
    /// 999 while the handler answers 9 fails here, and so does a handler key nobody announces.
    /// </summary>
    [Theory]
    [MemberData(nameof(SpokenMenus))]
    public void EverySpokenMenuNamesExactlyTheKeysThatAreAnswered(string where, string menu)
    {
        var answered = ScenarioDigitKeys();
        var spoken = Digits(menu);

        Assert.True(answered.SequenceEqual(spoken, StringComparer.Ordinal),
            $"{where} names key(s) [{string.Join(", ", spoken)}] but the product answers "
            + $"[{string.Join(", ", answered)}].\n\n\"{menu.Trim()}\"\n\n"
            + "A key spoken but not handled is answered with the invalid-key prompt; a key handled but "
            + "never spoken is a key nobody presses. Change both, or neither.");
    }

    /// <summary>Every menu the guard compares has to actually be found, or it passes by looking at nothing.</summary>
    [Fact]
    public void EveryPlaceTheMenuIsWrittenDownIsBeingRead()
    {
        var menus = SpokenMenus();

        Assert.NotEmpty(TtsDefaults.GetAvailableLanguages());
        Assert.True(menus.Count >= TtsDefaults.GetAvailableLanguages().Count + 3,
            $"only {menus.Count} key menus were found; one of the places the menu is written down is no "
            + "longer being read, so a drift there would go unnoticed.");
    }

    /// <summary>The two products that read the same menu have to answer the same keys.</summary>
    [Fact]
    public void TheSelfHostedAdapterAndTheScenarioAnswerTheSameKeys() =>
        Assert.Equal(ScenarioDigitKeys(), AdapterDigitKeys());

    /// <summary>The conference key, written out: an equality between two production values pins nothing.</summary>
    [Fact]
    public void TheConferenceKeyIsNine()
    {
        Assert.Equal("9", CalluVoiceRequestBuilder.ConferenceKey);
        Assert.Contains("9", ScenarioDigitKeys());
    }

    /// <summary>The guard's control group: a menu that does not match the handlers must be caught.</summary>
    [Theory]
    [InlineData("Press 1 to acknowledge, 2 to escalate, star to repeat, or 999 for video conference.")]
    [InlineData("Press 1 to acknowledge, 2 to escalate, or star to repeat.")]
    [InlineData("Press 1 to acknowledge, 2 to escalate, star to repeat, or 0 for video conference.")]
    public void TheGuardWouldNotice_AMenuThatDoesNotMatchTheHandlers(string drifted) =>
        Assert.NotEqual(ScenarioDigitKeys(), Digits(drifted));
}
