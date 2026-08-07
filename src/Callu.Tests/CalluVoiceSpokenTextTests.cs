using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;

namespace Callu.Tests;

/// <summary>What the call actually says, for someone answering it half asleep.</summary>
public class CalluVoiceSpokenTextTests
{
    private static Dictionary<string, string> Messages() => new()
    {
        ["incident_message"] = "Callu'dan olay bildirimi. {service} servisinde {severity_text} bir sorun var. {title}.",
        ["severity_critical"] = "kritik",
        ["severity_high"] = "yüksek öncelikli",
        ["dtmf_prompt"] = "Onaylamak için 1'e basın.",
    };

    private static MakeCallRequest Request(string? severity = "Critical", string? description = null) => new()
    {
        Destination = "+15550101",
        ServiceName = "Checkout API",
        IncidentTitle = "Ödeme geçidi yanıt vermiyor",
        Severity = severity,
        Description = description,
        Language = "tr-TR",
        DataLanguage = "tr-TR",
    };

    private static string Spoken(MakeCallRequest request, Dictionary<string, string>? messages = null) =>
        string.Join(" ", CalluVoiceRequestBuilder
            .Build(request, request.Destination, "call-1", messages ?? Messages(), null, null)
            .Announcement.Select(s => s.Text));

    // The severity is the most important word in the sentence, and it used to arrive as the raw enum:
    // a Turkish voice reading "Critical durumu tespit edildi".
    [Theory]
    [InlineData("Critical", "kritik")]
    [InlineData("High", "yüksek öncelikli")]
    public void SpeaksTheSeverityInTheLanguageTheCallIsIn(string severity, string expected)
    {
        var spoken = Spoken(Request(severity));

        Assert.Contains(expected, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain(severity, spoken, StringComparison.Ordinal);
    }

    /// <summary>A severity with no word of its own is still said, rather than leaving a hole.</summary>
    [Fact]
    public void FallsBackToTheRawSeverityWhenNoWordIsDefined()
    {
        Assert.Contains("Medium", Spoken(Request("Medium")), StringComparison.Ordinal);
    }

    // The description is free text from whatever raised the alert, and every character of it is read
    // before the keypad options — which is the part the person is waiting for.
    [Fact]
    public void ClipsADescriptionThatWouldBeReadForever()
    {
        var messages = Messages();
        messages["incident_message"] = "{description}";

        var spoken = Spoken(Request(description: new string('a', 5000)), messages);

        Assert.True(spoken.Length <= TtsDefaults.MaxSpokenDescriptionLength + 4, $"spoke {spoken.Length} characters");
        Assert.EndsWith("…", spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesAShortDescriptionWhole()
    {
        var messages = Messages();
        messages["incident_message"] = "{description}";

        Assert.Equal("3-D Secure geri dönüşü zaman aşımına uğradı", Spoken(
            Request(description: "3-D Secure geri dönüşü zaman aşımına uğradı"), messages));
    }

    /// <summary>The person is told who is calling before they are told anything else.</summary>
    [Fact]
    public void NamesTheProductThatIsCalling()
    {
        Assert.StartsWith("Callu", Spoken(Request()), StringComparison.Ordinal);
    }

    // A Turkish responder paged about an alert written in English hears one call in two languages: the
    // menu in theirs, the alert's own words in the language it was raised in. Every test above happens
    // to use one language for both, so nothing here proved the split actually happens.
    [Fact]
    public void SpeaksTheMenuAndTheIncidentTextInTheirOwnLanguages()
    {
        var request = Request();
        request.Language = "tr-TR";
        request.DataLanguage = "en-US";

        var built = CalluVoiceRequestBuilder.Build(
            request, request.Destination, "call-1", Messages(), null, null);

        var announcement = built.Announcement;

        Assert.Contains(announcement, s => s.Lang == "tr-TR" && s.Text.Contains("servisinde", StringComparison.Ordinal));
        Assert.Contains(announcement, s => s.Lang == "en-US" && s.Text.Contains("Checkout API", StringComparison.Ordinal));

        // The keypad instructions are menu vocabulary and never carry the alert's language.
        Assert.All(built.Prompt, s => Assert.Equal("tr-TR", s.Lang));
    }

    /// <summary>The language the messages resolved to wins over the one that was requested.</summary>
    // Resolution falls back to whatever template is marked default, and speaking those words in the
    // language nobody wrote them in is what reads Turkish digits out in English.
    [Fact]
    public void SpeaksThePromptsInTheLanguageTheMessagesResolvedTo()
    {
        var request = Request();
        request.Language = "en-US";

        var built = CalluVoiceRequestBuilder.Build(
            request, request.Destination, "call-1", Messages(), null, null, promptLanguage: "tr-TR");

        Assert.All(built.Prompt, s => Assert.Equal("tr-TR", s.Lang));
    }
}
