using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// The callu-voice adapter reports what a responder pressed through one channel and one only: the
/// callback URL in its config. Enabled without one it places calls, plays "the incident has been
/// acknowledged", and leaves the incident Open with no record to arm a further attempt from — so the
/// API refuses to enable it, and the refusal names the key that is missing.
/// </summary>
public class CalluVoiceEnableGateTests
{
    private const string BaseUrl = "http://callu-voice:8090";

    /// <summary>The whole of what an operator supplies: the address callu-voice can reach Callu on.</summary>
    private const string CallbackUrl = "https://callu.example.com";

    private sealed class Harness
    {
        public ICommunicationProviderRepository Providers { get; } =
            Substitute.For<ICommunicationProviderRepository>();

        public CommunicationProvider? Stored { get; private set; }

        public CommunicationProviderService Service { get; }

        public Harness(CommunicationProvider? existing = null)
        {
            var transactions = Substitute.For<ITransactionManager>();
            var registry = Substitute.For<ICommunicationProviderRegistry>();

            registry.GetAvailableProviderTypes().Returns(["callu-voice", "voximplant"]);

            transactions.ExecuteInTransactionAsync(Arg.Any<Func<Task<Guid>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<Guid>>>()());
            transactions.ExecuteInTransactionAsync(Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<bool>>>()());

            Providers.AddAsync(Arg.Do<CommunicationProvider>(p => Stored = p), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // No other provider exists here; the one-voice-service-one-carrier check reads through this.
            Providers.FindAsync(
                    Arg.Any<System.Linq.Expressions.Expression<Func<CommunicationProvider, bool>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(Array.Empty<CommunicationProvider>());

            if (existing is not null)
            {
                Stored = existing;
                Providers.FindSingleAsync(
                        Arg.Any<System.Linq.Expressions.Expression<Func<CommunicationProvider, bool>>>(),
                        Arg.Any<CancellationToken>())
                    .Returns(existing);
            }

            Service = new CommunicationProviderService(
                Providers,
                Substitute.For<ICapabilityProviderMappingRepository>(),
                transactions,
                registry,
                new ProviderSecretProtector(
                    new EphemeralDataProtectionProvider(), NullLogger<ProviderSecretProtector>.Instance),
                Substitute.For<IAuditLogService>(),
                NullLogger<CommunicationProviderService>.Instance);
        }
    }

    private static CreateProviderRequest Create(params (string Key, object Value)[] config) => new()
    {
        Name = "voice",
        ProviderType = "callu-voice",
        Config = config.ToDictionary(c => c.Key, c => c.Value)
    };

    // ------------------------------------------------------------------ create

    /// <summary>The whole point: an Admin cannot bring this provider up over the API without a callback.</summary>
    [Fact]
    public async Task CreatingItWithNoCallbackUrl_IsRefused_AndSaysWhichKeyIsMissing()
    {
        var harness = new Harness();

        var refused = await Assert.ThrowsAsync<Callu.Shared.Exceptions.ValidationException>(
            () => harness.Service.CreateProviderAsync(Create(("baseUrl", BaseUrl), ("apiToken", "t"))));

        Assert.Contains("callbackUrl", refused.Message, StringComparison.Ordinal);
        Assert.Null(harness.Stored);
        await harness.Providers.DidNotReceive()
            .AddAsync(Arg.Any<CommunicationProvider>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An empty string is the shape a form field with nothing typed into it arrives in.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatingItWithABlankCallbackUrl_IsRefusedToo(string blank)
    {
        var harness = new Harness();

        var refused = await Assert.ThrowsAsync<Callu.Shared.Exceptions.ValidationException>(
            () => harness.Service.CreateProviderAsync(
                Create(("baseUrl", BaseUrl), ("callbackUrl", blank))));

        Assert.Contains("callbackUrl", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// callu-voice posts the callback itself, from its own container, so a path or a scheme it cannot
    /// speak is exactly as dead as no URL at all — and dead in a way nobody notices until an incident.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/callbacks/callu-voice")]
    [InlineData("callu.example.com/hook")]
    [InlineData("ftp://callu.example.com/hook")]
    [InlineData("not a url at all")]
    public async Task CreatingItWithAnUnreachableCallbackUrl_IsRefused_AndQuotesTheValue(string unusable)
    {
        var harness = new Harness();

        var refused = await Assert.ThrowsAsync<Callu.Shared.Exceptions.ValidationException>(
            () => harness.Service.CreateProviderAsync(
                Create(("baseUrl", BaseUrl), ("callbackUrl", unusable))));

        Assert.Contains(unusable, refused.Message, StringComparison.Ordinal);
        Assert.Contains("http", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(harness.Stored);
    }

    [Fact]
    public async Task CreatingItWithAUsableCallbackUrl_IsAllowed()
    {
        var harness = new Harness();

        await harness.Service.CreateProviderAsync(
            Create(("baseUrl", BaseUrl), ("apiToken", "t"), ("callbackUrl", CallbackUrl)));

        Assert.NotNull(harness.Stored);
        Assert.Contains(CallbackUrl, harness.Stored!.ConfigJson, StringComparison.Ordinal);
    }

    /// <summary>The gate is this provider's, not every provider's.</summary>
    [Fact]
    public async Task AnotherProviderTypeIsNotHeldToTheCallbackRule()
    {
        var harness = new Harness();

        await harness.Service.CreateProviderAsync(new CreateProviderRequest
        {
            Name = "vox",
            ProviderType = "voximplant",
            Config = new Dictionary<string, object> { ["accountId"] = "1" }
        });

        Assert.NotNull(harness.Stored);
    }

    // ------------------------------------------------------------------ enable, on a row that already exists

    private static CommunicationProvider Existing(string configJson) => new()
    {
        Id = Guid.NewGuid(),
        Name = "voice",
        ProviderType = "callu-voice",
        IsEnabled = false,
        ConfigJson = configJson
    };

    /// <summary>
    /// The second door into the same room: a provider stored while disabled, then switched on with a
    /// request that carries no config at all. The stored keys are what would be used, so they are judged.
    /// </summary>
    [Fact]
    public async Task EnablingAStoredProviderThatHasNoCallbackUrl_IsRefused()
    {
        var harness = new Harness(Existing($"{{\"baseUrl\":\"{BaseUrl}\"}}"));

        var refused = await Assert.ThrowsAsync<Callu.Shared.Exceptions.ValidationException>(
            () => harness.Service.UpdateProviderAsync(
                harness.Stored!.Id, new UpdateProviderRequest { Name = "voice", IsEnabled = true }));

        Assert.Contains("callbackUrl", refused.Message, StringComparison.Ordinal);
        Assert.False(harness.Stored!.IsEnabled);
    }

    /// <summary>A stored callback URL satisfies the gate on its own — it does not have to be re-sent.</summary>
    [Fact]
    public async Task EnablingAStoredProviderThatAlreadyHasOne_IsAllowed()
    {
        var harness = new Harness(Existing($"{{\"baseUrl\":\"{BaseUrl}\",\"callbackUrl\":\"{CallbackUrl}\"}}"));

        await harness.Service.UpdateProviderAsync(
            harness.Stored!.Id, new UpdateProviderRequest { Name = "voice", IsEnabled = true });

        Assert.True(harness.Stored.IsEnabled);
    }

    /// <summary>Clearing the callback on a live provider is the same fault arriving from the other side.</summary>
    [Fact]
    public async Task ClearingTheCallbackUrlOnAnEnabledProvider_IsRefused()
    {
        var harness = new Harness(Existing($"{{\"baseUrl\":\"{BaseUrl}\",\"callbackUrl\":\"{CallbackUrl}\"}}"));

        await Assert.ThrowsAsync<Callu.Shared.Exceptions.ValidationException>(
            () => harness.Service.UpdateProviderAsync(harness.Stored!.Id, new UpdateProviderRequest
            {
                Name = "voice",
                IsEnabled = true,
                Config = new Dictionary<string, object> { ["callbackUrl"] = "" }
            }));

        Assert.Contains(CallbackUrl, harness.Stored!.ConfigJson, StringComparison.Ordinal);
    }

    /// <summary>Turning it OFF must always be possible, whatever its config looks like.</summary>
    [Fact]
    public async Task DisablingAMisconfiguredProviderIsNeverBlocked()
    {
        var harness = new Harness(Existing($"{{\"baseUrl\":\"{BaseUrl}\"}}"));

        await harness.Service.UpdateProviderAsync(
            harness.Stored!.Id, new UpdateProviderRequest { Name = "voice", IsEnabled = false });

        Assert.False(harness.Stored.IsEnabled);
    }

    // ------------------------------------------------------------------ the rule itself

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("/hook")]
    [InlineData("callu.example.com/hook")]
    [InlineData("ftp://callu.example.com/hook")]
    [InlineData("file:///tmp/hook")]
    public void AnAddressCalluVoiceCannotPostToIsRefused(string? callbackUrl) =>
        Assert.NotNull(CalluVoiceConfig.RefuseCallbackUrl(callbackUrl));

    /// <summary>
    /// An address that resolves and answers, but not on the path Callu serves call status on. callu-voice
    /// posts, gets a 404, retries for a few seconds and drops it — and the responder who pressed 1 has
    /// been told the incident was acknowledged while it stays Open. There is no settings field for this
    /// provider, so the path is something an operator types from memory: the gate has to be the check.
    /// </summary>
    [Theory]
    [InlineData("https://callu.example.com/api/v1/callbacks/callu-voice")]
    [InlineData("http://callu:8080/api/v1/callbacks/callu-voice")]
    [InlineData("https://callu.example.com/hook")]
    [InlineData("https://callu.example.com/api/callu-voice")]
    [InlineData("https://callu.example.com/api/callu-voice/callback/some-token")]
    public void AnAddressWhoseSuffixThisInstallationDoesNotServeIsRefused(string callbackUrl)
    {
        var refusal = CalluVoiceConfig.RefuseCallbackUrl(callbackUrl);

        Assert.NotNull(refusal);
        Assert.Contains(callbackUrl, refusal, StringComparison.Ordinal);
        Assert.Contains(CalluVoiceConfig.CallbackPath, refusal, StringComparison.Ordinal);
    }

    /// <summary>Anything already on the URL would be dropped when Callu appends its own path and token.</summary>
    [Theory]
    [InlineData("https://callu.example.com?src=voice")]
    [InlineData("https://callu.example.com#fragment")]
    [InlineData("https://admin:hunter2@callu.example.com")]
    public void AnAddressCarryingMoreThanAnAddressIsRefused(string callbackUrl) =>
        Assert.NotNull(CalluVoiceConfig.RefuseCallbackUrl(callbackUrl));

    /// <summary>Refusals are logged, so the one branch a credential can arrive in does not quote the value back.</summary>
    [Fact]
    public void TheRefusalForAnAddressCarryingCredentials_DoesNotRepeatThem()
    {
        var refusal = CalluVoiceConfig.RefuseCallbackUrl("https://admin:hunter2@callu.example.com");

        Assert.NotNull(refusal);
        Assert.DoesNotContain("hunter2", refusal, StringComparison.Ordinal);
    }

    /// <summary>The address alone is the shape; the path Callu already serves is tolerated and rebuilt anyway.</summary>
    [Theory]
    [InlineData("http://callu:8080")]
    [InlineData("https://callu.example.com")]
    [InlineData("https://callu.example.com/")]
    [InlineData("  https://callu.example.com  ")]
    [InlineData("https://callu.example.com/api/callu-voice/callback")]
    public void AnAbsoluteHttpAddressIsAccepted(string callbackUrl) =>
        Assert.Null(CalluVoiceConfig.RefuseCallbackUrl(callbackUrl));

    /// <summary>Whatever shape got through the gate, one address comes out — the route the API answers on.</summary>
    [Theory]
    [InlineData("https://callu.example.com")]
    [InlineData("https://callu.example.com/")]
    [InlineData("https://callu.example.com/api/callu-voice/callback")]
    public void EveryAcceptedAddressBuildsTheSameEndpoint(string callbackUrl) =>
        Assert.Equal(
            "https://callu.example.com" + CalluVoiceConfig.CallbackPath,
            CalluVoiceCallbackTokenProtector.CallbackUrlFor(callbackUrl, token: null));
}
