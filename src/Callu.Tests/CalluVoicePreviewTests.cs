using System.Net;
using System.Text.Json;
using Callu.Application.Services;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Rendering sample text without placing a call.</summary>
// Everything wrong with a spoken prompt used to be invisible until a phone rang: an unsubstituted
// placeholder, a number read in the wrong language. The normalized text the synthesizer reports is
// what makes those visible, so it has to survive the round trip intact.
public class CalluVoicePreviewTests
{
    private const string BaseUrl = "http://callu-voice:8090";

    private sealed record Sent(HttpMethod Method, string Path, string? Body, string? Authorization);

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Sent(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.Parameter));

            return respond(request);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (CalluVoiceProvider Provider, ScriptedHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string templateLanguage = "tr-TR")
    {
        var handler = new ScriptedHandler(respond);
        var protection = new EphemeralDataProtectionProvider();

        var provider = new CalluVoiceProvider(
            new StubHttpClientFactory(handler),
            TemplateResolvingTo(templateLanguage),
            new ProviderSecretProtector(protection, NullLogger<ProviderSecretProtector>.Instance),
            new SipTrunkPasswordProtector(protection, NullLogger<SipTrunkPasswordProtector>.Instance),
            protection,
            NullLogger<CalluVoiceProvider>.Instance);

        provider.InitializeAsync(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["baseUrl"] = BaseUrl,
                ["apiToken"] = "shared-secret",
                ["callbackUrl"] = "http://callu-api:5095",
                ["voice"] = "F1"
            }),
            sipTrunk: null).GetAwaiter().GetResult();

        return (provider, handler);
    }

    /// <summary>A resolver that always reports one language, the way a fallback to the default template does.</summary>
    private static ITtsTemplateService TemplateResolvingTo(string languageCode)
    {
        var service = Substitute.For<ITtsTemplateService>();
        service.ResolveMessagesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TtsResolvedMessages(languageCode, new Dictionary<string, string>
            {
                ["incident_message"] = "{service} servisinde {severity_text} bir sorun var. {title}.",
                ["dtmf_prompt"] = "Onaylamak için bire basın.",
                ["severity_critical"] = "kritik",
            }));
        return service;
    }

    private static HttpResponseMessage Rendered() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""
        {"segments":[
          {"key":"0123456789abcdef0123456789abcdef","normalized":"Onaylamak için bire basın.","duration_s":2.5}
        ]}
        """)
    };

    private static TtsPreviewRequest Request() => new()
    {
        Segments =
        [
            new TtsPreviewSegmentRequest { Text = "Onaylamak için 1'e basın.", Lang = "tr-TR" }
        ]
    };

    [Fact]
    public async Task TheNormalizedTextComesBack_SoAWrongReadingIsVisibleWithoutListening()
    {
        var (provider, _) = Build(_ => Rendered());

        var result = await provider.PreviewAsync(Request(), CancellationToken.None);

        Assert.True(result.Success);
        var segment = Assert.Single(result.Segments);
        Assert.Equal("Onaylamak için bire basın.", segment.Normalized);
        Assert.Equal("0123456789abcdef0123456789abcdef", segment.Key);
    }

    /// <summary>The segment's own language is what the whole preview is for; it may not be dropped.</summary>
    [Fact]
    public async Task EachSegmentsLanguageIsSent()
    {
        var (provider, handler) = Build(_ => Rendered());

        await provider.PreviewAsync(Request(), CancellationToken.None);

        var post = handler.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("/preview", post.Path);
        Assert.Contains("\"lang\":\"tr-TR\"", post.Body ?? "", StringComparison.Ordinal);
        Assert.Equal("shared-secret", post.Authorization);
    }

    /// <summary>A refusal is reported as one, rather than as an empty render nobody can explain.</summary>
    [Fact]
    public async Task AServiceThatRefuses_IsReportedWithItsReason()
    {
        var (provider, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"unknown voice 'Z9'"}""")
        });

        var result = await provider.PreviewAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("unknown voice", result.ErrorMessage ?? "", StringComparison.Ordinal);
    }

    /// <summary>The template preview goes through the real builder, so it cannot flatter a broken call.</summary>
    // Its whole value is agreeing with what a page would say; a second rendering of the same strings
    // would have shown the right language on the night the call used the wrong one.
    [Fact]
    public async Task ATemplatePreviewSpeaksTheLanguageTheTemplateResolvedTo()
    {
        var (provider, handler) = Build(_ => Rendered());

        await provider.PreviewTemplateAsync("en-US", CancellationToken.None);

        var post = handler.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("/preview", post.Path);
        // The stub resolver reports tr-TR whatever is asked for, exactly as a fallback to the default
        // template does; the segments must follow it rather than the language that was requested.
        Assert.Contains("\"lang\":\"tr-TR\"", post.Body ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("\"lang\":\"en-US\"", post.Body ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task AudioIsFetchedUnderTheKeyTheRenderReported()
    {
        var (provider, handler) = Build(request =>
            request.RequestUri!.AbsolutePath.StartsWith("/preview/audio/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("RIFF"u8.ToArray()) }
                : Rendered());

        await using var audio = await provider.PreviewAudioAsync("0123456789abcdef0123456789abcdef", CancellationToken.None);

        Assert.NotNull(audio);
        var get = handler.Requests.Single(r => r.Method == HttpMethod.Get);
        Assert.Equal("/preview/audio/0123456789abcdef0123456789abcdef", get.Path);
        Assert.Equal("shared-secret", get.Authorization);
    }

    /// <summary>Nothing rendered under that key is a "no", not a stream of nothing.</summary>
    [Fact]
    public async Task AudioForAnUnknownKey_IsNull()
    {
        var (provider, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await provider.PreviewAudioAsync("deadbeef", CancellationToken.None));
    }

    /// <summary>The sample text is spoken in the language the template resolved to.</summary>
    // Sample data left in one language is read by whatever voice the template asks for, so an English
    // template is heard pronouncing another language's words — a fault in the preview, not the template.
    [Fact]
    public async Task TheSampleTextFollowsTheResolvedLanguage()
    {
        Messages.InitializeFromJson("""
            {"providers": {
              "previewSampleTitle": "Payment service returning HTTP 503",
              "previewSampleService": "Checkout API",
              "previewSampleDescription": "The database connection pool is exhausted."}}
            """, "en");

        var (provider, handler) = Build(_ => Rendered(), templateLanguage: "en-US");

        await provider.PreviewTemplateAsync("en-US", CancellationToken.None);

        var post = handler.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Contains("Payment service returning HTTP 503", post.Body ?? "", StringComparison.Ordinal);
    }

    /// <summary>The synthesizer turning a preview away for a call in flight is not a refusal of the text.</summary>
    [Fact]
    public async Task APreviewYieldingToACall_ReportsWhyWithoutBlamingTheText()
    {
        var (provider, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"error":"a call is being rendered right now; try the preview again shortly"}""")
        });

        var result = await provider.PreviewAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(
            "a call is being rendered right now; try the preview again shortly", result.ErrorMessage);
    }
}
