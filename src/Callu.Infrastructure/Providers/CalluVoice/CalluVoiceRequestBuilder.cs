using System.Text.RegularExpressions;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;

namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>Builds the POST /calls body from an incident and the operator's spoken templates.</summary>
public static partial class CalluVoiceRequestBuilder
{
    public const string AcknowledgeKey = "1";
    public const string EscalateKey = "2";
    public const string RepeatKey = "*";

    /// <summary>The key that asks to be brought into the incident, the same one every spoken menu names.</summary>
    public const string ConferenceKey = "9";

    /// <summary>Announcement segments past which the split is collapsed back into one.</summary>
    // callu-voice refuses a request carrying more than 32 segments in total rather than dropping any,
    // and a refused request is a page nobody hears.
    internal const int MaxAnnouncementSegments = 24;

    /// <summary>Spoken when the announcement template resolves to nothing at all.</summary>
    private const string NoTemplateAnnouncement = "Callu incident notification.";

    /// <summary>Spoken when the key-menu template resolves to nothing at all.</summary>
    internal const string NoTemplatePrompt =
        "Press 1 to acknowledge, 2 to escalate, star to repeat, or 9 to ask to be brought in.";

    /// <summary>Spoken for the conference key when its template resolves to nothing at all.</summary>
    // This service bridges nobody: it records the request, reports it and hangs up, so the confirmation
    // may promise only that. Saying nothing is worse — an empty response hangs up mid-menu in silence.
    internal const string NoTemplateConferenceConfirmation =
        "Your request has been recorded. The details will be on the incident in Callu.";

    [GeneratedRegex(@"\{(service|severity_text|severity|title|description)\}")]
    private static partial Regex Placeholder();

    public static CalluVoiceCallRequest Build(
        MakeCallRequest request,
        string destination,
        string callId,
        IReadOnlyDictionary<string, string> messages,
        string? voice,
        string? callbackUrl,
        string? promptLanguage = null)
    {
        // The menu is read to the person being called, so it follows their language; the incident text
        // keeps whatever language it was written in.
        // promptLanguage is the language the messages actually resolved to, which is not the requested
        // one when resolution fell back to another template.
        var promptLang = Language(promptLanguage ?? request.Language);
        var dataLang = Language(request.DataLanguage);

        var prompt = Spoken(Template(messages, "dtmf_prompt"), promptLang);
        if (prompt.Count == 0)
            prompt = [new CalluVoiceSegment(NoTemplatePrompt, promptLang)];

        var conference = Spoken(Template(messages, "conference_requested"), promptLang);
        if (conference.Count == 0)
            conference = [new CalluVoiceSegment(NoTemplateConferenceConfirmation, promptLang)];

        return new CalluVoiceCallRequest(
            callId,
            destination,
            Announcement(request, messages, promptLang, dataLang),
            prompt,
            new CalluVoiceResponses(
                Spoken(Template(messages, "ack_confirm"), promptLang),
                Spoken(Template(messages, "escalation_confirm"), promptLang),
                conference,
                Spoken(Template(messages, "invalid_key"), promptLang)),
            new CalluVoiceKeys(AcknowledgeKey, EscalateKey, RepeatKey, ConferenceKey))
        {
            Voice = Trimmed(voice),
            CallbackUrl = Trimmed(callbackUrl)
        };
    }

    private static IReadOnlyList<CalluVoiceSegment> Announcement(
        MakeCallRequest request,
        IReadOnlyDictionary<string, string> messages,
        string promptLang,
        string dataLang)
    {
        var severityWord = TtsDefaults.SeverityWord(messages, request.Severity);

        // Substituted before the language split: severity is menu vocabulary, not incident text, so it
        // belongs to the language the person is being spoken to in.
        var template = Template(messages, "incident_message")?
            .Replace("{severity_text}", severityWord, StringComparison.Ordinal);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["service"] = request.ServiceName ?? string.Empty,
            ["severity"] = Or(request.Severity, "Medium"),
            ["severity_text"] = severityWord,
            ["title"] = Or(request.IncidentTitle, "Incident alert"),
            ["description"] = TtsDefaults.ClipDescription(request.Description)
        };

        var rendered = template is null
            ? []
            : Render(template, values, promptLang, dataLang);

        return rendered.Count > 0 ? rendered : WithoutATemplate(request, promptLang, dataLang);
    }

    /// <summary>Every incident field the call still carries when no template survived.</summary>
    private static List<CalluVoiceSegment> WithoutATemplate(
        MakeCallRequest request, string promptLang, string dataLang)
    {
        List<CalluVoiceSegment> segments = [new(NoTemplateAnnouncement, promptLang)];

        foreach (var value in new[] { request.ServiceName, request.Severity, request.IncidentTitle, request.Description })
        {
            if (!string.IsNullOrWhiteSpace(value))
                segments.Add(new CalluVoiceSegment(value.Trim(), dataLang));
        }

        return segments;
    }

    /// <summary>Substitutes the incident into the template, splitting per language only when the two differ.</summary>
    // One segment per language boundary is how a Turkish alert keeps a Turkish voice inside an English
    // menu; splitting when there is no boundary would only chop one sentence into pieces.
    private static List<CalluVoiceSegment> Render(
        string template,
        IReadOnlyDictionary<string, string> values,
        string promptLang,
        string dataLang)
    {
        if (string.Equals(promptLang, dataLang, StringComparison.OrdinalIgnoreCase))
            return Spoken(Placeholder().Replace(template, m => values[m.Groups[1].Value]), promptLang);

        var segments = new List<CalluVoiceSegment>();
        var cursor = 0;

        foreach (Match match in Placeholder().Matches(template))
        {
            segments.AddRange(Spoken(template[cursor..match.Index], promptLang));
            segments.AddRange(Spoken(values[match.Groups[1].Value], dataLang));
            cursor = match.Index + match.Length;
        }
        segments.AddRange(Spoken(template[cursor..], promptLang));

        if (segments.Count <= MaxAnnouncementSegments)
            return segments;

        return Spoken(Placeholder().Replace(template, m => values[m.Groups[1].Value]), promptLang);
    }

    private static List<CalluVoiceSegment> Spoken(string? text, string lang) =>
        string.IsNullOrWhiteSpace(text) ? [] : [new CalluVoiceSegment(text.Trim(), lang)];

    private static string? Template(IReadOnlyDictionary<string, string> messages, string key) =>
        messages.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string Language(string? code) =>
        string.IsNullOrWhiteSpace(code) ? SupportedCultures.Fallback : code.Trim();

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();



    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
