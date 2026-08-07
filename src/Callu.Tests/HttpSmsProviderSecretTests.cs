using System.Net;
using System.Text.Json;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.HttpSms;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>The http-sms provider decrypts its stored credentials, and still tolerates plaintext ones.</summary>
public class HttpSmsProviderSecretTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"m1\"}") };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static ProviderSecretProtector Protector(IDataProtectionProvider provider) =>
        new(provider, NullLogger<ProviderSecretProtector>.Instance);

    private static string ConfigJson(string apiKey, string username, string password) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["url"] = "https://sms.example.com/send",
            ["method"] = "POST",
            ["contentType"] = "json",
            ["headers"] = new Dictionary<string, string> { ["Authorization"] = "Bearer {apiKey}" },
            ["bodyTemplate"] = "{\"user\":\"{username}\",\"pass\":\"{password}\",\"to\":\"{to}\",\"text\":\"{message}\"}",
            ["apiKey"] = apiKey,
            ["username"] = username,
            ["password"] = password,
        });

    private static async Task<CapturingHandler> SendAsync(string configJson, ProviderSecretProtector protector)
    {
        var handler = new CapturingHandler();
        var provider = new HttpSmsProvider(
            new StubHttpClientFactory(handler),
            protector,
            NullLogger<HttpSmsProvider>.Instance);

        await provider.InitializeAsync(configJson, sipTrunk: null);
        var result = await provider.SendSmsAsync(new SendSmsRequest { To = "+905550000000", Message = "incident" });

        Assert.True(result.Success);
        return handler;
    }

    [Fact]
    public async Task Encrypted_Credentials_Reach_The_Gateway_As_Plaintext()
    {
        var dp = new EphemeralDataProtectionProvider();
        var protector = Protector(dp);

        var configJson = ConfigJson(
            protector.Protect("live-api-key"),
            protector.Protect("live-user"),
            protector.Protect("live-pass"));

        // Guard the premise: the stored config really is ciphertext.
        Assert.Contains(ProviderSecretProtector.CipherPrefix, configJson);

        var handler = await SendAsync(configJson, protector);

        Assert.Equal("Bearer live-api-key", handler.Request!.Headers.GetValues("Authorization").Single());
        Assert.Contains("\"user\":\"live-user\"", handler.Body);
        Assert.Contains("\"pass\":\"live-pass\"", handler.Body);
    }

    [Fact]
    public async Task Ciphertext_Never_Reaches_The_Gateway()
    {
        var dp = new EphemeralDataProtectionProvider();
        var protector = Protector(dp);

        var handler = await SendAsync(
            ConfigJson(protector.Protect("k"), protector.Protect("u"), protector.Protect("p")),
            protector);

        var authorization = handler.Request!.Headers.GetValues("Authorization").Single();
        Assert.DoesNotContain(ProviderSecretProtector.CipherPrefix, authorization);
        Assert.DoesNotContain(ProviderSecretProtector.CipherPrefix, handler.Body);
    }

    /// <summary>Configs written before the secrets were encrypted (and rollbacks) must still send.</summary>
    [Fact]
    public async Task Plaintext_Credentials_Are_Passed_Through_Unchanged()
    {
        var protector = Protector(new EphemeralDataProtectionProvider());

        var handler = await SendAsync(ConfigJson("plain-key", "plain-user", "plain-pass"), protector);

        Assert.Equal("Bearer plain-key", handler.Request!.Headers.GetValues("Authorization").Single());
        Assert.Contains("\"user\":\"plain-user\"", handler.Body);
        Assert.Contains("\"pass\":\"plain-pass\"", handler.Body);
    }
}
