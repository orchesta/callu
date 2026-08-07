using System.Net;
using System.Text.Json;
using Callu.Application.Services;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Two languages that must not bleed into each other: the prompts are spoken in the
/// recipient's, the incident text in the one it was written in.</summary>
public class VoximplantCallLanguageTests
{
    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"result":1,"media_session_access_url":"https://vox/session"}""")
            });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new OkHandler(), disposeHandler: false);
    }

    /// <summary>Captures the VoxCallData the provider hands to the call-token store — the scenario reads it verbatim.</summary>
    private static async Task<VoxCallData> CallDataForAsync(MakeCallRequest request, string? resolvedLanguage = null)
    {
        var callDataService = Substitute.For<ICallDataService>();
        VoxCallData? captured = null;
        callDataService
            .CreateCallTokenAsync(Arg.Do<VoxCallData>(d => captured = d), Arg.Any<CancellationToken>())
            .Returns("call-token");

        var tts = Substitute.For<ITtsTemplateService>();
        tts.ResolveMessagesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TtsResolvedMessages(
                resolvedLanguage ?? call.Arg<string>(),
                new Dictionary<string, string>
                {
                    // Echo the language the provider resolved, so the choice is observable.
                    ["greeting"] = $"[{call.Arg<string>()}] {{title}} / {{severity}}"
                })));

        var dataProtection = new EphemeralDataProtectionProvider();
        var provider = new VoximplantProvider(
            new StubHttpClientFactory(),
            NullLogger<VoximplantProvider>.Instance,
            callDataService,
            tts,
            new ProviderSecretProtector(dataProtection, NullLogger<ProviderSecretProtector>.Instance),
            new SipTrunkPasswordProtector(dataProtection, NullLogger<SipTrunkPasswordProtector>.Instance));

        await provider.InitializeAsync(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["accountId"] = 1,
                ["apiKey"] = "key",
                ["incidentCallRuleId"] = 42,
            }),
            sipTrunk: null);

        var result = await provider.MakeCallAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(captured);
        return captured!;
    }

    private static string Greeting(VoxCallData callData)
    {
        Assert.NotNull(callData.TtsMessages);
        return callData.TtsMessages["greeting"];
    }

    private static MakeCallRequest Call(string? language, string? dataLanguage) => new()
    {
        IncidentId = Guid.NewGuid(),
        Destination = "+905551112233",
        IncidentTitle = "Checkout failing",
        Severity = "Critical",
        ServiceName = "Payments",
        Language = language,
        DataLanguage = dataLanguage,
    };

    /// <summary>The prompts follow the person being called, whatever language the alert arrived in.
    /// A Turkish responder paged about an English alert is prompted in Turkish.</summary>
    [Fact]
    public async Task ThePromptsFollowTheRecipient_NotTheIncident()
    {
        var callData = await CallDataForAsync(Call(language: "tr-TR", dataLanguage: "en-US"));

        Assert.Equal("tr-TR", callData.Language);
        Assert.Contains("[tr-TR]", Greeting(callData));
    }

    /// <summary>The bug this replaced: the prompt language fell back to the incident's, and since the
    /// incident text defaults to en-US, every call was read out in English.</summary>
    [Fact]
    public async Task TheIncidentsLanguageNeverDecidesThePrompts()
    {
        var callData = await CallDataForAsync(Call(language: "tr-TR", dataLanguage: "de-DE"));

        Assert.Equal("tr-TR", callData.Language);
        Assert.DoesNotContain("[de-DE]", Greeting(callData));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithNoRecipientLanguage_TheFallbackIsSpoken_NotTheIncidents(string? language)
    {
        var callData = await CallDataForAsync(Call(language, dataLanguage: "de-DE"));

        Assert.Equal("en-US", callData.Language);
    }

    /// <summary>An operator adds a language by saving a TTS template, so the provider must not cap
    /// what it will speak to the set the product happens to ship.</summary>
    [Fact]
    public async Task ALanguageTheProductDoesNotShipIsStillSpoken()
    {
        var callData = await CallDataForAsync(Call(language: "de-DE", dataLanguage: "en-US"));

        Assert.Equal("de-DE", callData.Language);
        Assert.Contains("[de-DE]", Greeting(callData));
    }

    /// <summary>
    /// The frame is spoken in the resolved language; the interpolated incident text is wrapped in
    /// the incident's own DataLanguage so an English title is not read out with a Turkish voice.
    /// </summary>
    [Fact]
    public async Task TheIncidentText_IsWrappedInTheIncidentsOwnLanguage()
    {
        var callData = await CallDataForAsync(Call(language: "tr-TR", dataLanguage: "en-US"));

        Assert.Equal("tr-TR", callData.Language);
        Assert.Contains("""<lang xml:lang="en-US">Checkout failing</lang>""", Greeting(callData));
    }

    // Only one template is configured and it is not the language that was asked for. The scenario is
    // told the language of the words it was handed, or it reads them with the wrong voice.
    [Fact]
    public async Task ResolutionFallingBackToAnotherTemplate_DecidesTheSpokenLanguage()
    {
        var callData = await CallDataForAsync(
            Call(language: "en-US", dataLanguage: "en-US"), resolvedLanguage: "tr-TR");

        Assert.Equal("tr-TR", callData.Language);
    }
}
