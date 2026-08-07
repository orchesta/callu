using Callu.Domain.Entities;
using System.Net;
using System.Text.Json;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// The callu-voice adapter: what each answer to POST /calls means for the page, what body goes out,
/// and what happens to a phone number that is not in international form.
/// </summary>
public class CalluVoiceProviderTests
{
    private const string BaseUrl = "http://callu-voice:8090";
    private const string Callee = "+905321234567";
    /// <summary>What an operator configures: the address alone. Callu owns everything after it.</summary>
    private const string CallbackUrl = "https://callu.example.com";

    private static readonly string CallbackEndpoint = CallbackUrl + CalluVoiceConfig.CallbackPath;

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body, string? Authorization)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.Method,
                request.RequestUri!,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString()));

            return respond(request);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>The messages Callu ships, so the tests speak what an untouched install would speak.</summary>
    private static Dictionary<string, string> ShippedMessages() => new()
    {
        ["incident_message"] = "Alert from {service}. There is a {severity_text} issue. {title}. {description}.",
        ["dtmf_prompt"] = "Press 1 to acknowledge, 2 to escalate, star to repeat, or 9 for video conference.",
        ["ack_confirm"] = "Got it. The incident has been acknowledged. Thank you.",
        ["escalation_confirm"] = "Understood. Escalation has been initiated.",
        ["invalid_key"] = "Sorry, that key is not valid. Please try again.",
        ["conference_requested"] =
            "Your request has been recorded. The details will be on the incident in Callu. This call will now end."
    };

    private static HttpResponseMessage Answer(int status, string body = "{}") =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    private static (CalluVoiceProvider Provider, ScriptedHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        Dictionary<string, string>? messages = null,
        string? callbackUrl = CallbackUrl,
        string? voice = null,
        string apiToken = "shared-secret",
        bool encryptToken = false,
        IDataProtectionProvider? keyring = null,
        string? resolvedLanguage = null)
    {
        var handler = new ScriptedHandler(respond);

        var tts = Substitute.For<ITtsTemplateService>();
        tts.ResolveMessagesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TtsResolvedMessages(
                resolvedLanguage ?? call.Arg<string>(), messages ?? ShippedMessages())));

        var protector = new ProviderSecretProtector(
            new EphemeralDataProtectionProvider(), NullLogger<ProviderSecretProtector>.Instance);

        var provider = new CalluVoiceProvider(
            new StubHttpClientFactory(handler), tts, protector,
            new SipTrunkPasswordProtector(new EphemeralDataProtectionProvider(), NullLogger<SipTrunkPasswordProtector>.Instance), keyring ?? new EphemeralDataProtectionProvider(), NullLogger<CalluVoiceProvider>.Instance);

        var config = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["baseUrl"] = BaseUrl,
            ["apiToken"] = encryptToken ? protector.Protect(apiToken) : apiToken,
            ["callbackUrl"] = callbackUrl,
            ["voice"] = voice,
            ["requestTimeoutSeconds"] = 30
        });

        provider.InitializeAsync(config, sipTrunk: null).GetAwaiter().GetResult();
        return (provider, handler);
    }

    private static MakeCallRequest Page(Guid? attemptId = null) => new()
    {
        Destination = Callee,
        AttemptId = attemptId,
        IncidentId = Guid.NewGuid(),
        IncidentTitle = "PostgreSQL connection pool exhausted",
        Severity = "Critical",
        ServiceName = "payment-api",
        Description = "Every write is timing out.",
        Language = "en-US",
        DataLanguage = "en-US"
    };

    private static JsonElement SentBody(ScriptedHandler handler)
    {
        var post = handler.Requests.Single(r => r.Method == HttpMethod.Post);
        return JsonDocument.Parse(post.Body!).RootElement.Clone();
    }

    // ------------------------------------------------------------------ the code → answer table

    /// <summary>
    /// Every status code callu-voice can answer POST /calls with, and whether a call went out. This
    /// is the table the retry cadence is built on: reading "refused" as "unknown" parks a page that
    /// could go out again in seconds, and reading "unknown" as "refused" re-dials a ringing phone.
    /// </summary>
    [Theory]
    // Accepted: the page is on its way.
    [InlineData(202, CalluVoiceDialAnswer.Placed)]
    // A call with this id is already running, so one is on its way even though this attempt placed none.
    [InlineData(409, CalluVoiceDialAnswer.AlreadyInFlight)]
    // The request cannot be spoken, or the token is wrong, or the body is too big. Nothing was dialled.
    [InlineData(400, CalluVoiceDialAnswer.Refused)]
    [InlineData(401, CalluVoiceDialAnswer.Refused)]
    [InlineData(413, CalluVoiceDialAnswer.Refused)]
    [InlineData(429, CalluVoiceDialAnswer.Refused)]
    // Documented as "nothing was dialled": the dial did not leave, or the service is full or draining.
    [InlineData(502, CalluVoiceDialAnswer.Refused)]
    [InlineData(503, CalluVoiceDialAnswer.Refused)]
    // Nothing callu-voice sends. Whatever answered says nothing about whether a phone is ringing.
    [InlineData(200, CalluVoiceDialAnswer.Undetermined)]
    [InlineData(500, CalluVoiceDialAnswer.Undetermined)]
    [InlineData(504, CalluVoiceDialAnswer.Undetermined)]
    public void EachStatusCodeSaysWhetherACallWentOut(int status, CalluVoiceDialAnswer expected) =>
        Assert.Equal(expected, CalluVoiceDialAnswers.ForStatusCode(status));

    /// <summary>
    /// A refusal is answered, not thrown. Throwing makes the caller treat a service that plainly
    /// said "nothing was dialled" as a dial whose outcome is unknown.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(413)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task ARefusalIsReportedAsAFailedCall_NotThrown(int status)
    {
        var (provider, _) = Build(_ => Answer(status, "{\"error\":\"at capacity\"}"));

        var result = await provider.MakeCallAsync(Page());

        Assert.False(result.Success);
        Assert.Contains(status.ToString(), result.ErrorMessage!);
        Assert.Contains("at capacity", result.ErrorMessage!);
    }

    [Fact]
    public async Task AnAcceptedCallIsReportedAsPlaced_WithTheIdCalluVoiceEchoed()
    {
        var (provider, _) = Build(_ => Answer(202, "{\"call_id\":\"abc123\",\"status\":\"placing\"}"));

        var result = await provider.MakeCallAsync(Page());

        Assert.True(result.Success);
        Assert.Equal("abc123", result.CallId);
    }

    /// <summary>
    /// A duplicate means a call for this id is ringing right now and will report on itself. Calling
    /// that a failure re-dials a responder who is already being called.
    /// </summary>
    [Fact]
    public async Task ACallAlreadyInFlightCountsAsPlaced()
    {
        var (provider, _) = Build(_ => Answer(409, "{\"error\":\"call already in progress\"}"));

        var result = await provider.MakeCallAsync(Page());

        Assert.True(result.Success);
        Assert.NotNull(result.CallId);
    }

    /// <summary>Nothing callu-voice documents: the caller must not be told the call was refused.</summary>
    [Theory]
    [InlineData(500)]
    [InlineData(504)]
    public async Task AnAnswerThatSaysNothingIsThrown(int status)
    {
        var (provider, _) = Build(_ => Answer(status));

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.MakeCallAsync(Page()));
    }

    /// <summary>
    /// The prompts are rendered before POST /calls answers, so a timeout can sit on either side of a
    /// phone that is already ringing.
    /// </summary>
    [Fact]
    public async Task ATimeoutIsThrown_BecauseTheCallMayHaveGoneOut()
    {
        var (provider, _) = Build(_ => throw new TaskCanceledException("HttpClient.Timeout"));

        var thrown = await Assert.ThrowsAsync<TimeoutException>(() => provider.MakeCallAsync(Page()));

        Assert.Contains("unknown", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A connection that never came up is the shape a restarting voice container takes, and it is as
    /// certain as a 503 that nothing was dialled.
    /// </summary>
    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public async Task AConnectionThatNeverCameUpIsAFailedCall_NotAnUnknownOne(HttpRequestError error)
    {
        var (provider, _) = Build(_ => throw new HttpRequestException(error, "connection refused"));

        var result = await provider.MakeCallAsync(Page());

        Assert.False(result.Success);
        Assert.Contains("could not be reached", result.ErrorMessage!);
    }

    /// <summary>A connection lost with the request already sent could have placed the call.</summary>
    [Fact]
    public async Task AConnectionLostAfterTheRequestWentOutStaysUnknown()
    {
        var (provider, _) = Build(_ => throw new HttpRequestException(HttpRequestError.ResponseEnded, "closed"));

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.MakeCallAsync(Page()));
    }

    // ------------------------------------------------------------------ the body callu-voice reads

    /// <summary>callu-voice refuses a body carrying a field it does not know, so the set is pinned.</summary>
    [Fact]
    public async Task TheBodyCarriesOnlyFieldsCalluVoiceDeclares()
    {
        string[] allowed =
            ["call_id", "to", "voice", "gate", "announcement", "prompt", "responses", "keys", "limits", "callback_url"];

        var (provider, handler) = Build(_ => Answer(202), callbackUrl: CallbackUrl, voice: "F1");
        await provider.MakeCallAsync(Page());

        var body = SentBody(handler);
        Assert.All(body.EnumerateObject(), p => Assert.Contains(p.Name, allowed));

        Assert.Equal(
            ["acknowledged", "conference", "escalated", "invalid_key"],
            body.GetProperty("responses").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["acknowledge", "conference", "escalate", "repeat"],
            body.GetProperty("keys").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(
            body.GetProperty("announcement").EnumerateArray(),
            s => Assert.Equal(["lang", "text"], s.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
    }

    // ------------------------------------------------------------------ the id the duplicate guard reads

    /// <summary>
    /// callu-voice refuses a second call carrying an id it already has in flight, and that guard is
    /// the only thing standing between an ambiguous timeout and a responder's phone ringing twice.
    /// A fresh id per dial made it unreachable, so the id is the page attempt's own.
    /// </summary>
    [Fact]
    public async Task TheSameAttemptDialledTwiceCarriesTheSameCallId()
    {
        var attempt = Guid.NewGuid();

        var (first, firstHandler) = Build(_ => Answer(202));
        await first.MakeCallAsync(Page(attempt));

        var (second, secondHandler) = Build(_ => Answer(202));
        await second.MakeCallAsync(Page(attempt));

        Assert.Equal(
            SentBody(firstHandler).GetProperty("call_id").GetString(),
            SentBody(secondHandler).GetProperty("call_id").GetString());
    }

    /// <summary>The other direction: a genuinely new attempt must not be mistaken for a repeat of the last one.</summary>
    [Fact]
    public async Task ADifferentAttemptCarriesADifferentCallId()
    {
        var (first, firstHandler) = Build(_ => Answer(202));
        await first.MakeCallAsync(Page(Guid.NewGuid()));

        var (second, secondHandler) = Build(_ => Answer(202));
        await second.MakeCallAsync(Page(Guid.NewGuid()));

        Assert.NotEqual(
            SentBody(firstHandler).GetProperty("call_id").GetString(),
            SentBody(secondHandler).GetProperty("call_id").GetString());
    }

    /// <summary>A dial with no attempt behind it — the test-call button — still gets an id of its own.</summary>
    [Fact]
    public async Task ADialWithNoAttemptBehindItStillCarriesAnId()
    {
        var (first, firstHandler) = Build(_ => Answer(202));
        await first.MakeCallAsync(Page());

        var (second, secondHandler) = Build(_ => Answer(202));
        await second.MakeCallAsync(Page());

        var one = SentBody(firstHandler).GetProperty("call_id").GetString();
        var other = SentBody(secondHandler).GetProperty("call_id").GetString();

        Assert.False(string.IsNullOrWhiteSpace(one));
        Assert.NotEqual(one, other);
    }

    /// <summary>The four groups callu-voice reads, and the two it will not place a call without.</summary>
    // The installation configured one template, in a language nobody asked for. Sending those words
    // under the requested language is what hands Turkish text to an English voice.
    [Fact]
    public async Task ThePromptsCarryTheLanguageTheMessagesResolvedTo()
    {
        var (provider, handler) = Build(_ => Answer(202), resolvedLanguage: "tr-TR");
        await provider.MakeCallAsync(Page());

        var prompt = SentBody(handler).GetProperty("prompt").EnumerateArray().ToArray();

        Assert.NotEmpty(prompt);
        Assert.All(prompt, segment => Assert.Equal("tr-TR", segment.GetProperty("lang").GetString()));
    }

    [Fact]
    public async Task TheRequestCarriesACallIdADestinationAnAnnouncementAndAPrompt()
    {
        var (provider, handler) = Build(_ => Answer(202));
        await provider.MakeCallAsync(Page());

        var body = SentBody(handler);

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("call_id").GetString()));
        Assert.Equal(Callee, body.GetProperty("to").GetString());
        Assert.NotEmpty(body.GetProperty("announcement").EnumerateArray());
        Assert.NotEmpty(body.GetProperty("prompt").EnumerateArray());
        Assert.NotEmpty(body.GetProperty("responses").GetProperty("acknowledged").EnumerateArray());
        Assert.NotEmpty(body.GetProperty("responses").GetProperty("escalated").EnumerateArray());
        Assert.NotEmpty(body.GetProperty("responses").GetProperty("invalid_key").EnumerateArray());
    }

    /// <summary>
    /// The conference key is 9, spelled out here rather than compared to the constant that produced
    /// it — a guard that reads the production value on both sides passes whatever that value becomes.
    /// </summary>
    [Fact]
    public async Task TheConferenceKeyIsNine_Literally()
    {
        var (provider, handler) = Build(_ => Answer(202));
        await provider.MakeCallAsync(Page());

        Assert.Equal("9", SentBody(handler).GetProperty("keys").GetProperty("conference").GetString());
    }

    /// <summary>Every key, written out, so a swap between two of them is not silently accepted either.</summary>
    [Theory]
    [InlineData("acknowledge", "1")]
    [InlineData("escalate", "2")]
    [InlineData("repeat", "*")]
    [InlineData("conference", "9")]
    public async Task EveryKeyIsTheOneTheMenuNames(string action, string expected)
    {
        var (provider, handler) = Build(_ => Answer(202));
        await provider.MakeCallAsync(Page());

        Assert.Equal(expected, SentBody(handler).GetProperty("keys").GetProperty(action).GetString());
    }

    /// <summary>A claim the shipped conference confirmation may not make, keyed by the locale it is written in.</summary>
    private static readonly Dictionary<string, string> ConnectionClaim = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en-US"] = "connect",
        ["tr-TR"] = "bağlan",
    };

    /// <summary>Every shipped locale's own conference confirmation, so a rewrite of the JSON file is what this test checks — not a copy the test file keeps of its own.</summary>
    public static TheoryData<string, string> ShippedConferenceConfirmations()
    {
        TtsDefaults.Initialize(Path.Combine(AppContext.BaseDirectory, "Resources", "TtsDefaults"));

        var data = new TheoryData<string, string>();
        foreach (var language in TtsDefaults.GetAvailableLanguages())
        {
            var text = TtsDefaults.GetDefaults(language).GetValueOrDefault("conference_requested");
            Assert.False(string.IsNullOrWhiteSpace(text), $"{language}.json ships no conference_requested template");
            data.Add(language, text!);
        }

        return data;
    }

    /// <summary>
    /// The key is live: pressing it ends the call. With nothing to speak for it the service hangs up
    /// in silence, so the confirmation must exist — and must promise only what actually happens. This
    /// reads the shipped locale file itself, so a rewritten confirmation that lies about a connection
    /// fails here even though the request builder renders it unchanged.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippedConferenceConfirmations))]
    public async Task TheConferenceKeyIsAnsweredWithSomethingTrue_NotSilence(string language, string shippedText)
    {
        var (provider, handler) = Build(_ => Answer(202), messages: new Dictionary<string, string>
        {
            ["conference_requested"] = shippedText
        });
        await provider.MakeCallAsync(Page());

        var spoken = string.Join(" ", SentBody(handler)
            .GetProperty("responses").GetProperty("conference").EnumerateArray()
            .Select(s => s.GetProperty("text").GetString()));

        Assert.False(string.IsNullOrWhiteSpace(spoken));
        Assert.Equal(shippedText, spoken);

        // Nothing is bridged by this provider, so the shipped confirmation may not claim anybody is
        // being connected — in whichever language it ships in.
        if (ConnectionClaim.TryGetValue(language, out var claim))
            Assert.DoesNotContain(claim, spoken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A missing template must not turn the live key into a silent hangup.</summary>
    [Fact]
    public async Task TheConferenceConfirmationSurvivesAnEmptyTemplateSet()
    {
        var (provider, handler) = Build(_ => Answer(202), messages: []);
        await provider.MakeCallAsync(Page());

        Assert.NotEmpty(SentBody(handler).GetProperty("responses").GetProperty("conference").EnumerateArray());
    }

    /// <summary>callu-voice's own key rule: one DTMF character each, and no two the same.</summary>
    [Fact]
    public async Task EveryKeyIsOneDtmfCharacterAndNoTwoCollide()
    {
        var (provider, handler) = Build(_ => Answer(202));
        await provider.MakeCallAsync(Page());

        var keys = SentBody(handler).GetProperty("keys").EnumerateObject()
            .Select(p => p.Value.GetString()!)
            .ToList();

        Assert.All(keys, k => Assert.Matches("^[0-9*#]$", k));
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>No gate is sent in this cut, so the announcement plays as soon as the call is answered.</summary>
    [Fact]
    public async Task NoGateIsSent()
    {
        var (provider, handler) = Build(_ => Answer(202));
        await provider.MakeCallAsync(Page());

        Assert.False(SentBody(handler).TryGetProperty("gate", out _));
    }

    /// <summary>
    /// The address callu-voice is told to post to is the one the API answers on — Callu builds the path,
    /// the operator does not guess it — with this call's own sealed token on it. A callback with no token
    /// is never applied, and one aimed at a path nothing serves is a 404 the service retries and forgets:
    /// either way the responder hears the incident acknowledged while it stays open.
    /// </summary>
    [Fact]
    public async Task TheCallbackUrlSentCarriesATokenThatResolvesBackToThisCall()
    {
        var keyring = new EphemeralDataProtectionProvider();
        var (provider, handler) = Build(_ => Answer(202), keyring: keyring);

        var page = Page(Guid.NewGuid());
        await provider.MakeCallAsync(page);

        var body = SentBody(handler);
        var sent = new Uri(body.GetProperty("callback_url").GetString()!);

        Assert.Equal(CalluVoiceConfig.CallbackPath, sent.AbsolutePath);

        var token = System.Web.HttpUtility.ParseQueryString(sent.Query)
            [CalluVoiceCallbackTokenProtector.TokenQueryKey];

        Assert.False(string.IsNullOrEmpty(token));
        Assert.True(new CalluVoiceCallbackTokenProtector(keyring).TryResolve(token, out var ticket));
        Assert.Equal(page.IncidentId, ticket.IncidentId);
        Assert.Equal(Callee, ticket.PhoneNumber);

        // What the token says the call is, and what the body says it is, have to agree — the endpoint
        // refuses a callback whose body names a call the token was not minted for.
        Assert.Equal(body.GetProperty("call_id").GetString(), ticket.CallId);
        Assert.Equal(page.AttemptId!.Value.ToString("N"), ticket.CallId);
    }

    /// <summary>
    /// The secret is a bearer credential, so it travels in the one part of a request line neither the
    /// bundled nginx nor this API's own exception logging writes down: never the path.
    /// </summary>
    [Fact]
    public async Task TheTokenIsNowhereInThePathOfTheCallbackUrlSent()
    {
        var keyring = new EphemeralDataProtectionProvider();
        var (provider, handler) = Build(_ => Answer(202), keyring: keyring);

        await provider.MakeCallAsync(Page(Guid.NewGuid()));

        var sent = new Uri(SentBody(handler).GetProperty("callback_url").GetString()!);
        var token = System.Web.HttpUtility.ParseQueryString(sent.Query)
            [CalluVoiceCallbackTokenProtector.TokenQueryKey]!;

        Assert.DoesNotContain(token, sent.GetLeftPart(UriPartial.Path), StringComparison.Ordinal);
        Assert.Equal(CallbackEndpoint, sent.GetLeftPart(UriPartial.Path));
    }

    /// <summary>A test call has no incident to bind a token to, so it goes out on the address with no token on it.</summary>
    [Fact]
    public async Task ATestCallWithNoIncidentSendsTheCallbackAddressWithNoToken()
    {
        var (provider, handler) = Build(_ => Answer(202));

        await provider.MakeCallAsync(new MakeCallRequest { Destination = Callee, Language = "en-US" });

        Assert.Equal(CallbackEndpoint, SentBody(handler).GetProperty("callback_url").GetString());
    }

    /// <summary>
    /// A call this service cannot report on is worse than no call: the responder hears the incident
    /// acknowledged, it stays open, and nothing writes the record a further attempt would be armed from.
    /// So the dial does not happen at all.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/api/v1/callbacks/callu-voice")]
    [InlineData("ftp://callu.example.com/hook")]
    // A path this API does not serve is as dead as no address at all, so the dial is refused the same way.
    [InlineData("https://callu.example.com/api/v1/callbacks/callu-voice")]
    [InlineData("https://callu.example.com/hook")]
    public async Task NoUsableCallbackUrlMeansNothingIsDialled(string? callbackUrl)
    {
        var (provider, handler) = Build(_ => Answer(202), callbackUrl: callbackUrl);

        var result = await provider.MakeCallAsync(Page());

        Assert.False(result.Success);
        Assert.Empty(handler.Requests);
        Assert.Contains("callbackUrl", result.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>
    /// callu-voice will not place a call with nothing to announce, and a page lost to a blank
    /// template is a page nobody hears.
    /// </summary>
    [Fact]
    public async Task ThereIsAlwaysSomethingToSay_EvenWithNoTemplatesAtAll()
    {
        var (provider, handler) = Build(_ => Answer(202), messages: []);
        await provider.MakeCallAsync(Page());

        var body = SentBody(handler);
        var announcement = body.GetProperty("announcement").EnumerateArray()
            .Select(s => s.GetProperty("text").GetString()!)
            .ToList();
        var prompt = string.Join(" ", body.GetProperty("prompt").EnumerateArray()
            .Select(s => s.GetProperty("text").GetString()));

        Assert.NotEmpty(announcement);
        Assert.Contains(announcement, t => t.Contains("PostgreSQL connection pool exhausted", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(prompt));
        // The stand-in menu names every key the request actually binds, 9 included.
        Assert.Contains('9', prompt);
    }

    /// <summary>The incident keeps its own language while the menu follows the person being called.</summary>
    [Fact]
    public async Task IncidentTextInAnotherLanguageIsSpokenInThatLanguage()
    {
        var (provider, handler) = Build(_ => Answer(202));

        var page = Page();
        page.Language = "en-US";
        page.DataLanguage = "tr-TR";
        await provider.MakeCallAsync(page);

        var announcement = SentBody(handler).GetProperty("announcement").EnumerateArray().ToList();

        Assert.Contains(announcement, s => s.GetProperty("lang").GetString() == "tr-TR"
                                           && s.GetProperty("text").GetString() == "payment-api");
        Assert.Contains(announcement, s => s.GetProperty("lang").GetString() == "en-US");
    }

    /// <summary>One language means one sentence: splitting it would only put gaps in mid-sentence.</summary>
    [Fact]
    public async Task OneLanguageIsSpokenAsOneSegment()
    {
        var (provider, handler) = Build(_ => Answer(202));
        await provider.MakeCallAsync(Page());

        var announcement = SentBody(handler).GetProperty("announcement").EnumerateArray().ToList();

        Assert.Single(announcement);
        Assert.Contains("payment-api", announcement[0].GetProperty("text").GetString()!, StringComparison.Ordinal);
        Assert.Contains("Critical", announcement[0].GetProperty("text").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Over 32 segments callu-voice refuses the whole request rather than dropping any, so a
    /// placeholder-heavy template must collapse instead of costing the page.
    /// </summary>
    [Fact]
    public async Task ATemplateFullOfPlaceholdersStillFitsInsideTheSegmentCap()
    {
        var crowded = ShippedMessages();
        crowded["incident_message"] = string.Concat(Enumerable.Repeat(
            "{service} {severity} {title} {description}. ", 12));

        var (provider, handler) = Build(_ => Answer(202), messages: crowded);

        var page = Page();
        page.Language = "en-US";
        page.DataLanguage = "tr-TR";
        await provider.MakeCallAsync(page);

        var body = SentBody(handler);
        var total = body.GetProperty("announcement").GetArrayLength()
                    + body.GetProperty("prompt").GetArrayLength()
                    + body.GetProperty("responses").EnumerateObject().Sum(p => p.Value.GetArrayLength());

        Assert.True(total <= 32, $"the request carries {total} segments");
    }

    // ------------------------------------------------------------------ the number, at the boundary

    /// <summary>
    /// A number callu-voice would answer with a 400 never leaves Callu: the 400 produces no callback
    /// at all, so the responder is unreachable with nothing to show for it.
    /// </summary>
    [Fact]
    public async Task ANumberThatIsNotE164IsRefusedHere_AndNothingIsSent()
    {
        var (provider, handler) = Build(_ => Answer(202));

        var page = Page();
        page.Destination = "0532 123 45 67";
        var result = await provider.MakeCallAsync(page);

        Assert.False(result.Success);
        Assert.Empty(handler.Requests);
        Assert.Contains("0532 123 45 67", result.ErrorMessage!);
        Assert.Contains("E.164", result.ErrorMessage!);
    }

    [Fact]
    public async Task AFormattedNumberIsDialledInTheShapeCalluVoiceAccepts()
    {
        var (provider, handler) = Build(_ => Answer(202));

        var page = Page();
        page.Destination = "0090 532 123 45 67";
        await provider.MakeCallAsync(page);

        Assert.Equal(Callee, SentBody(handler).GetProperty("to").GetString());
    }

    // ------------------------------------------------------------------ identity, secrets, wiring

    [Fact]
    public void TheProviderAnswersToItsRegisteredNameAndClaimsOnlyWhatItDoes()
    {
        var (provider, _) = Build(_ => Answer(202));

        Assert.Equal("callu-voice", provider.ProviderType);
        Assert.Equal(CommunicationCapability.VoiceCalls, provider.Capabilities);
    }

    /// <summary>
    /// Everything this provider does not do is refused rather than answered quietly, so a caller
    /// cannot be told a conference exists, or a call was hung up, when neither happened.
    /// </summary>
    [Fact]
    public async Task WhatTheProviderCannotDoIsRefused()
    {
        var (provider, handler) = Build(_ => Answer(202));

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.HangupCallAsync("c1"));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CreateConferenceAsync(new CreateConferenceRequest { Name = "war-room" }));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.SendSmsAsync(new SendSmsRequest { To = Callee, Message = "x" }));

        Assert.Empty(handler.Requests);
    }

    /// <summary>The token is stored encrypted, so it has to be decrypted before it is presented.</summary>
    [Fact]
    public async Task TheStoredTokenIsDecryptedBeforeItIsPresented()
    {
        var (provider, handler) = Build(_ => Answer(202), apiToken: "live-token", encryptToken: true);

        await provider.MakeCallAsync(Page());

        var authorization = handler.Requests.Single().Authorization;
        Assert.Equal("Bearer live-token", authorization);
        Assert.DoesNotContain(ProviderSecretProtector.CipherPrefix, authorization!);
    }

    [Fact]
    public async Task TheCallGoesToTheCallsEndpointUnderTheConfiguredBaseUrl()
    {
        var (provider, handler) = Build(_ => Answer(202));

        await provider.MakeCallAsync(Page());

        Assert.Equal(new Uri($"{BaseUrl}/calls"), handler.Requests.Single().Uri);
    }

    // ------------------------------------------------------------------ the connection test

    private const string HealthyBody = "{\"status\":\"ok\",\"active_calls\":0,\"checks\":{\"ari\":\"ok\"}}";

    private static HttpResponseMessage HealthThen(HttpRequestMessage request, int probeStatus) =>
        request.Method == HttpMethod.Get
            ? Answer(200, HealthyBody)
            : Answer(probeStatus, "{\"error\":\"call_id is required\"}");

    /// <summary>A token that will be rejected on every page must be visible before an incident finds it.</summary>
    [Fact]
    public async Task TheConnectionTestChecksTheTokenWithoutPlacingACall()
    {
        var (provider, handler) = Build(r => HealthThen(r, 400));

        var (success, message) = await provider.TestConnectionAsync();

        Assert.True(success, message);
        var probe = handler.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("{}", probe.Body);
        Assert.Equal("Bearer shared-secret", probe.Authorization);
    }

    [Fact]
    public async Task ARejectedTokenIsReportedAsSuch()
    {
        var (provider, _) = Build(r => HealthThen(r, 401));

        var (success, message) = await provider.TestConnectionAsync();

        Assert.False(success);
        Assert.Contains("token", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AServiceThatCannotPlaceCallsIsNotReportedAsConnected()
    {
        var (provider, _) = Build(_ => Answer(503, "{\"status\":\"unavailable\",\"active_calls\":0,\"checks\":{\"tts\":\"loading\"}}"));

        var (success, message) = await provider.TestConnectionAsync();

        Assert.False(success);
        Assert.Contains("cannot place calls", message);
    }

    /// <summary>A base URL pointing at something else must not read as a working voice provider.</summary>
    [Fact]
    public async Task SomethingThatIsNotCalluVoiceIsNotMistakenForIt()
    {
        var (provider, handler) = Build(_ => Answer(200, "{\"status\":\"ok\"}"));

        var (success, message) = await provider.TestConnectionAsync();

        Assert.False(success);
        Assert.Contains("base URL", message);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }
}


/// <summary>The carrier is Callu's to own; the voice service holds none of its own.</summary>
// It used to live only in the container's environment, so changing carriers meant recreating it.
public class CalluVoiceTrunkPushTests
{
    private static (CalluVoiceProvider Provider, List<HttpRequestMessage> Seen) Build(
        HttpStatusCode answer, SipTrunkSettings? trunk)
    {
        var seen = new List<HttpRequestMessage>();
        var handler = new RecordingHandler(seen, answer);

        var keyring = new EphemeralDataProtectionProvider();
        var secrets = new ProviderSecretProtector(keyring, NullLogger<ProviderSecretProtector>.Instance);
        var trunkProtector = new SipTrunkPasswordProtector(keyring, NullLogger<SipTrunkPasswordProtector>.Instance);

        if (trunk is not null && trunk.Password is not null)
            trunk.Password = trunkProtector.Protect(trunk.Password);

        var provider = new CalluVoiceProvider(
            new StubHttpClientFactory(handler),
            Substitute.For<ITtsTemplateService>(),
            secrets,
            trunkProtector,
            keyring,
            NullLogger<CalluVoiceProvider>.Instance);

        provider.InitializeAsync(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["baseUrl"] = "http://callu-voice:8090",
                ["apiToken"] = secrets.Protect("tok"),
                ["callbackUrl"] = "http://callu-api:5095",
            }), trunk).GetAwaiter().GetResult();

        return (provider, seen);
    }

    private static SipTrunkSettings Carrier() => new()
    {
        Name = "Carrier A", Server = "sip.carrier.example", Port = 5060,
        Username = "900001", Password = "s3cr3t", IsEnabled = true,
    };

    [Fact]
    public async Task TheCarrierIsSentToTheVoiceService_WithItsPasswordDecrypted()
    {
        var (provider, seen) = Build(HttpStatusCode.OK, Carrier());

        var (applied, error) = await provider.ApplyTrunkAsync();

        Assert.True(applied, error);
        var request = Assert.Single(seen);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith("/trunk", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("\"host\":\"sip.carrier.example\"", body, StringComparison.Ordinal);
        // Decrypted on the way out: the service cannot use a value sealed with Callu's key.
        Assert.Contains("\"password\":\"s3cr3t\"", body, StringComparison.Ordinal);
    }

    /// <summary>The voice service rejects a body carrying a field name it does not know.</summary>
    // A serializer default that camel-cased these would be refused whole, taking the carrier with it.
    [Fact]
    public async Task TheSeparateAuthUserAndCallerIdKeepTheNamesTheServiceReads()
    {
        var carrier = Carrier();
        carrier.AuthUser = "900001-auth";
        carrier.CallerId = "+905321234567";

        var (provider, seen) = Build(HttpStatusCode.OK, carrier);
        await provider.ApplyTrunkAsync();

        var body = await Assert.Single(seen).Content!.ReadAsStringAsync();
        using var sent = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal("900001-auth", sent.RootElement.GetProperty("auth_username").GetString());
        Assert.Equal("+905321234567", sent.RootElement.GetProperty("caller_id").GetString());
    }

    /// <summary>No carrier configured is a state the service has to be told about.</summary>
    // Otherwise removing a trunk in the panel leaves the previous one dialling.
    [Fact]
    public async Task NoTrunkSendsAnExplicitlyDisabledCarrier()
    {
        var (provider, seen) = Build(HttpStatusCode.OK, null);

        await provider.ApplyTrunkAsync();

        var body = await Assert.Single(seen).Content!.ReadAsStringAsync();
        Assert.Contains("\"enabled\":false", body, StringComparison.Ordinal);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ADisabledTrunkIsNotSentAsIfItWereLive()
    {
        var trunk = Carrier();
        trunk.IsEnabled = false;

        var (provider, seen) = Build(HttpStatusCode.OK, trunk);
        await provider.ApplyTrunkAsync();

        var body = await Assert.Single(seen).Content!.ReadAsStringAsync();
        Assert.Contains("\"enabled\":false", body, StringComparison.Ordinal);
    }

    /// <summary>The panel offers a transport, so the service has to be told which one.</summary>
    // Sent rather than dropped: a trunk saved as TLS and applied as plaintext UDP puts the SIP
    // credentials on the wire in the clear while the panel says otherwise.
    [Fact]
    public async Task TheSelectedTransportIsSentRatherThanAssumed()
    {
        var tcp = Carrier();
        tcp.UseTcp = true;

        var (provider, seen) = Build(HttpStatusCode.OK, tcp);
        await provider.ApplyTrunkAsync();

        var body = await Assert.Single(seen).Content!.ReadAsStringAsync();
        Assert.Contains("\"transport\":\"tcp\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TlsIsSentAsTlsSoTheVoiceServiceCanRefuseIt()
    {
        var tls = Carrier();
        tls.UseTls = true;

        var (provider, seen) = Build(HttpStatusCode.OK, tls);
        await provider.ApplyTrunkAsync();

        var body = await Assert.Single(seen).Content!.ReadAsStringAsync();
        Assert.Contains("\"transport\":\"tls\"", body, StringComparison.Ordinal);
    }

    /// <summary>What Callu cannot express is left unstated, not overwritten with a guess.</summary>
    // The keypress mode and whether to register are set on the voice container; stating them here
    // would reset them on every save, and a keypress that is never detected is a page unacknowledged.
    [Fact]
    public async Task SettingsCalluCannotExpressAreNotSentAtAll()
    {
        var (provider, seen) = Build(HttpStatusCode.OK, Carrier());

        await provider.ApplyTrunkAsync();

        var body = await Assert.Single(seen).Content!.ReadAsStringAsync();
        Assert.DoesNotContain("dtmf_mode", body, StringComparison.Ordinal);
        Assert.DoesNotContain("codecs", body, StringComparison.Ordinal);
        Assert.DoesNotContain("register", body, StringComparison.Ordinal);
    }

    /// <summary>A pasted hostname commonly carries a trailing space, and Asterisk strips it anyway.</summary>
    [Fact]
    public async Task ThePastedCarrierIsTrimmedOnTheWayOut()
    {
        var padded = Carrier();
        padded.Server = "  sip.carrier.example  ";
        padded.Username = " 900001 ";

        var (provider, seen) = Build(HttpStatusCode.OK, padded);
        await provider.ApplyTrunkAsync();

        var body = await Assert.Single(seen).Content!.ReadAsStringAsync();
        Assert.Contains("\"host\":\"sip.carrier.example\"", body, StringComparison.Ordinal);
        Assert.Contains("\"username\":\"900001\"", body, StringComparison.Ordinal);
    }

    /// <summary>Removing the provider has to remove the carrier, not leave it registering.</summary>
    [Fact]
    public async Task ClearingSendsAnExplicitlyDisabledCarrier()
    {
        var (provider, seen) = Build(HttpStatusCode.OK, Carrier());

        var (applied, error) = await provider.ClearTrunkAsync();

        Assert.True(applied, error);
        var body = await Assert.Single(seen).Content!.ReadAsStringAsync();
        Assert.Contains("\"enabled\":false", body, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", body, StringComparison.Ordinal);
    }

    /// <summary>A refusal is reported rather than swallowed, but does not throw.</summary>
    [Fact]
    public async Task ARefusedCarrierIsReported()
    {
        var (provider, _) = Build(HttpStatusCode.BadRequest, Carrier());

        var (applied, error) = await provider.ApplyTrunkAsync();

        Assert.False(applied);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(List<HttpRequestMessage> seen, HttpStatusCode answer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Read now: the content is disposed with the request once SendAsync returns.
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var copy = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = new StringContent(body),
            };
            seen.Add(copy);
            return new HttpResponseMessage(answer) { Content = new StringContent("{}") };
        }
    }
}


/// <summary>The fingerprint both sides compute, which is the whole of how a lost carrier is noticed.</summary>
// The vectors are what the Go implementation produces. If either side changes what the digest
// covers, this goes red rather than the two quietly disagreeing forever.
public class CalluVoiceTrunkFingerprintTests
{
    private const string GoEnabled = "b4e433398e15b2d4f140b90954ead318c8f724709f465e690313954fd4dc3cd3";
    private const string GoDisabled = "6568baf17e48def127420c2f460ec40b06b63553251aafa7945ff10ddbc8afbe";
    private const string GoBare = "72a52647c5147084f231edf46ab30b5b2fba0523d2cb8192ec2551a1f14de599";

    private static SipTrunkSettings Carrier() => new()
    {
        Name = "Carrier A", Server = "sip.carrier.example", Port = 5060,
        Username = "900001", IsEnabled = true,
    };

    [Fact]
    public void AFullCarrierHashesTheSameOnBothSides()
    {
        var trunk = Carrier();
        trunk.AuthUser = "auth900001";
        trunk.CallerId = "+905551112233";

        Assert.Equal(GoEnabled, CalluVoiceTrunk.From(trunk, "s3cr3t").Fingerprint());
    }

    [Fact]
    public void ACarrierWithNoSeparateAuthUserOrCallerIdHashesTheSameOnBothSides()
    {
        Assert.Equal(GoBare, CalluVoiceTrunk.From(Carrier(), "s3cr3t").Fingerprint());
    }

    /// <summary>"No carrier" is a state with its own fingerprint, so an unset trunk is comparable too.</summary>
    [Fact]
    public void HavingNoCarrierHashesTheSameOnBothSides()
    {
        Assert.Equal(GoDisabled, CalluVoiceTrunk.None.Fingerprint());
        Assert.Equal(GoDisabled, CalluVoiceTrunk.From(null, string.Empty).Fingerprint());
    }

    /// <summary>A switched-off trunk row is no carrier, not a carrier that happens to be off.</summary>
    [Fact]
    public void ASwitchedOffTrunkHashesAsNoCarrier()
    {
        var off = Carrier();
        off.IsEnabled = false;

        Assert.Equal(GoDisabled, CalluVoiceTrunk.From(off, "s3cr3t").Fingerprint());
    }

    /// <summary>A rotated SIP password is the case this feature exists for, and it is not in the body read back.</summary>
    [Fact]
    public void RotatingThePasswordChangesTheFingerprint()
    {
        var before = CalluVoiceTrunk.From(Carrier(), "s3cr3t").Fingerprint();
        var after = CalluVoiceTrunk.From(Carrier(), "rotated").Fingerprint();

        Assert.NotEqual(before, after);
    }

    /// <summary>It is logged and compared over an internal hop, so it must not carry the carrier.</summary>
    [Fact]
    public void TheFingerprintDisclosesNothing()
    {
        var print = CalluVoiceTrunk.From(Carrier(), "s3cr3t").Fingerprint();

        Assert.Equal(64, print.Length);
        Assert.DoesNotContain("s3cr3t", print, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("carrier", print, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whitespace is stripped before both the body and the digest, so they cannot describe different carriers.</summary>
    [Fact]
    public void PaddingIsStrippedBeforeTheDigestIsTaken()
    {
        var padded = Carrier();
        padded.Server = "  sip.carrier.example  ";
        padded.Username = " 900001 ";

        Assert.Equal(
            CalluVoiceTrunk.From(Carrier(), "s3cr3t").Fingerprint(),
            CalluVoiceTrunk.From(padded, " s3cr3t ").Fingerprint());
    }
}


/// <summary>Noticing that the voice service is no longer holding the carrier Callu configured.</summary>
// The service keeps no carrier across a restart, and until something compared the two, a container
// recreate left the panel showing a carrier that existed nowhere and no page leaving the box.
public class CalluVoiceTrunkReconciliationTests
{
    private static SipTrunkSettings Carrier() => new()
    {
        Name = "Carrier A", Server = "sip.carrier.example", Port = 5060,
        Username = "900001", Password = "s3cr3t", IsEnabled = true,
    };

    private sealed record World(
        CalluVoiceProvider Provider,
        List<HttpRequestMessage> Seen);

    private static World Build(
        SipTrunkSettings? trunk,
        Func<HttpRequestMessage, HttpResponseMessage> answerGet,
        HttpStatusCode putAnswer = HttpStatusCode.OK)
    {
        var seen = new List<HttpRequestMessage>();

        var keyring = new EphemeralDataProtectionProvider();
        var secrets = new ProviderSecretProtector(keyring, NullLogger<ProviderSecretProtector>.Instance);
        var trunkProtector = new SipTrunkPasswordProtector(keyring, NullLogger<SipTrunkPasswordProtector>.Instance);

        if (trunk?.Password is not null)
            trunk.Password = trunkProtector.Protect(trunk.Password);

        var handler = new TrunkHandler(seen, answerGet, putAnswer);

        var provider = new CalluVoiceProvider(
            new SingleHandlerFactory(handler),
            Substitute.For<ITtsTemplateService>(),
            secrets,
            trunkProtector,
            keyring,
            NullLogger<CalluVoiceProvider>.Instance);

        provider.InitializeAsync(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["baseUrl"] = "http://callu-voice:8090",
                ["apiToken"] = secrets.Protect("tok"),
                ["callbackUrl"] = "http://callu-api:5095",
            }), trunk).GetAwaiter().GetResult();

        return new World(provider, seen);
    }

    private static HttpResponseMessage Reports(string fingerprint) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"enabled\":true,\"fingerprint\":\"{fingerprint}\"}}"),
        };

    /// <summary>The ordinary sweep: nothing has changed, so nothing is sent and nothing is written.</summary>
    // Re-pushing a carrier that is already there would reload PJSIP every few minutes for no reason,
    // and an audit row per sweep would bury the one sweep that mattered.
    [Fact]
    public async Task AVoiceServiceStillHoldingTheCarrierIsLeftAlone()
    {
        var trunk = Carrier();
        var expected = CalluVoiceTrunk.From(trunk, "s3cr3t").Fingerprint();
        var world = Build(trunk, _ => Reports(expected));

        var result = await world.Provider.ReconcileTrunkAsync();

        Assert.Equal(CalluVoiceTrunkOutcome.InAgreement, result.Outcome);
        Assert.DoesNotContain(world.Seen, r => r.Method == HttpMethod.Put);
    }

    /// <summary>The defect this whole thing exists for: the container restarted and lost the carrier.</summary>
    [Fact]
    public async Task ACarrierTheVoiceServiceHasLostIsSentAgain()
    {
        var world = Build(Carrier(), _ => Reports(CalluVoiceTrunk.None.Fingerprint()));

        var result = await world.Provider.ReconcileTrunkAsync();

        Assert.Equal(CalluVoiceTrunkOutcome.Restored, result.Outcome);
        var put = Assert.Single(world.Seen, r => r.Method == HttpMethod.Put);
        var body = await put.Content!.ReadAsStringAsync();
        Assert.Contains("\"host\":\"sip.carrier.example\"", body, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"s3cr3t\"", body, StringComparison.Ordinal);
    }

    /// <summary>A SIP password rotated in the panel while the service was unreachable is the same fault.</summary>
    [Fact]
    public async Task ACarrierTheVoiceServiceHasStaleIsSentAgain()
    {
        var stale = Carrier();
        stale.Password = "previous";
        var staleFingerprint = CalluVoiceTrunk.From(stale, "previous").Fingerprint();

        var world = Build(Carrier(), _ => Reports(staleFingerprint));

        var result = await world.Provider.ReconcileTrunkAsync();

        Assert.Equal(CalluVoiceTrunkOutcome.Restored, result.Outcome);
        var put = Assert.Single(world.Seen, r => r.Method == HttpMethod.Put);
        Assert.Contains("\"password\":\"s3cr3t\"", await put.Content!.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>Unreachable says nothing about the carrier, so nothing may be concluded and nothing sent.</summary>
    [Fact]
    public async Task AVoiceServiceThatCannotBeReadIsNotPushedAt()
    {
        var world = Build(Carrier(), _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("{}"),
        });

        var result = await world.Provider.ReconcileTrunkAsync();

        Assert.Equal(CalluVoiceTrunkOutcome.CouldNotRead, result.Outcome);
        Assert.DoesNotContain(world.Seen, r => r.Method == HttpMethod.Put);
    }

    /// <summary>A service too old to report a fingerprint is unknown, not empty.</summary>
    // Read as "no carrier" it would be re-pushed on every sweep for as long as it ran.
    [Fact]
    public async Task AnAnswerWithNoFingerprintIsARefusalToConclude()
    {
        var world = Build(Carrier(), _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"enabled\":true,\"host\":\"sip.carrier.example\"}"),
        });

        var result = await world.Provider.ReconcileTrunkAsync();

        Assert.Equal(CalluVoiceTrunkOutcome.CouldNotRead, result.Outcome);
        Assert.DoesNotContain(world.Seen, r => r.Method == HttpMethod.Put);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    /// <summary>Known wrong and could not be fixed is the state an operator has to be told about.</summary>
    [Fact]
    public async Task ACarrierThatCouldNotBeRestoredSaysSo()
    {
        var world = Build(
            Carrier(),
            _ => Reports(CalluVoiceTrunk.None.Fingerprint()),
            putAnswer: HttpStatusCode.BadRequest);

        var result = await world.Provider.ReconcileTrunkAsync();

        Assert.Equal(CalluVoiceTrunkOutcome.CouldNotRestore, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TrunkHandler(
        List<HttpRequestMessage> seen,
        Func<HttpRequestMessage, HttpResponseMessage> answerGet,
        HttpStatusCode putAnswer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            seen.Add(new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = new StringContent(body),
            });

            return request.Method == HttpMethod.Get
                ? answerGet(request)
                : new HttpResponseMessage(putAnswer) { Content = new StringContent("{}") };
        }
    }
}
