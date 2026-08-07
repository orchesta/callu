using System.Text.Json;
using Callu.Application.Services;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

public class VoximplantMissingRuleTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("HTTP must not be called when the rule is missing");
    }

    [Fact]
    public async Task MakeCall_WithoutIncidentRule_FailsBeforeCallingVox()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var provider = new VoximplantProvider(
            new StubHttpClientFactory(),
            NullLogger<VoximplantProvider>.Instance,
            Substitute.For<ICallDataService>(),
            Substitute.For<ITtsTemplateService>(),
            new ProviderSecretProtector(dataProtection, NullLogger<ProviderSecretProtector>.Instance),
            new SipTrunkPasswordProtector(dataProtection, NullLogger<SipTrunkPasswordProtector>.Instance));

        await provider.InitializeAsync(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["accountId"] = 1,
                ["apiKey"] = "key",
            }),
            sipTrunk: null);

        var result = await provider.MakeCallAsync(new MakeCallRequest
        {
            Destination = "+15555550100",
            IncidentId = Guid.NewGuid(),
            IncidentTitle = "Checkout failing",
        });

        Assert.False(result.Success);
        Assert.Contains("rule is not provisioned", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
