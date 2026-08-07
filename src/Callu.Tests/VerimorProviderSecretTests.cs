using System.Net;
using System.Text.Json;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.Verimor;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>The Verimor provider decrypts its stored credentials instead of handing the gateway the ciphertext.</summary>
public class VerimorProviderSecretTests
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
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("123456789") };
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static ProviderSecretProtector Protector(IDataProtectionProvider provider) =>
        new(provider, NullLogger<ProviderSecretProtector>.Instance);

    private static string ConfigJson(string username, string password) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["apiUsername"] = username,
            ["apiPassword"] = password,
            ["senderId"] = "CALLU",
        });

    private static async Task<(CapturingHandler Handler, VerimorProvider Provider)> InitializeAsync(
        string configJson, ProviderSecretProtector protector)
    {
        var handler = new CapturingHandler();
        var provider = new VerimorProvider(
            new StubHttpClientFactory(handler),
            protector,
            NullLogger<VerimorProvider>.Instance);

        await provider.InitializeAsync(configJson, sipTrunk: null);
        return (handler, provider);
    }

    [Fact]
    public async Task Encrypted_Credentials_Reach_The_Gateway_As_Plaintext()
    {
        var protector = Protector(new EphemeralDataProtectionProvider());
        var configJson = ConfigJson(protector.Protect("live-user"), protector.Protect("live-pass"));

        // Guard the premise: the stored config really is ciphertext.
        Assert.Contains(ProviderSecretProtector.CipherPrefix, configJson);

        var (handler, provider) = await InitializeAsync(configJson, protector);
        var result = await provider.SendSmsAsync(new SendSmsRequest { To = "+905550000000", Message = "incident" });

        Assert.True(result.Success);
        Assert.Contains("\"username\":\"live-user\"", handler.Body);
        Assert.Contains("\"password\":\"live-pass\"", handler.Body);
    }

    [Fact]
    public async Task Ciphertext_Never_Reaches_The_Gateway()
    {
        var protector = Protector(new EphemeralDataProtectionProvider());

        var (handler, provider) = await InitializeAsync(
            ConfigJson(protector.Protect("u"), protector.Protect("p")), protector);
        await provider.SendSmsAsync(new SendSmsRequest { To = "+905550000000", Message = "incident" });

        Assert.DoesNotContain(ProviderSecretProtector.CipherPrefix, handler.Body);
    }

    /// <summary>The balance probe carries the credentials in the query string — same secret, other path.</summary>
    [Fact]
    public async Task TestConnection_SendsPlaintextCredentials_NotCiphertext()
    {
        var protector = Protector(new EphemeralDataProtectionProvider());

        var (handler, provider) = await InitializeAsync(
            ConfigJson(protector.Protect("live-user"), protector.Protect("live-pass")), protector);

        var (success, _) = await provider.TestConnectionAsync();

        Assert.True(success);
        var url = handler.Request!.RequestUri!.ToString();
        Assert.Contains("username=live-user", url);
        Assert.Contains("password=live-pass", url);
        Assert.DoesNotContain("enc%3Av1", url);
    }

    /// <summary>Configs written before the secrets were encrypted (and rollbacks) must still send.</summary>
    [Fact]
    public async Task Plaintext_Credentials_Are_Passed_Through_Unchanged()
    {
        var protector = Protector(new EphemeralDataProtectionProvider());

        var (handler, provider) = await InitializeAsync(ConfigJson("plain-user", "plain-pass"), protector);
        await provider.SendSmsAsync(new SendSmsRequest { To = "+905550000000", Message = "incident" });

        Assert.Contains("\"username\":\"plain-user\"", handler.Body);
        Assert.Contains("\"password\":\"plain-pass\"", handler.Body);
    }
}
