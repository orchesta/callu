using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Infrastructure.Providers.Voximplant.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Voximplant;

public class VoximplantScenarioKeyValidator(
    IDbContextFactory<ApplicationDbContext> contextFactory,
    ProviderSecretProtector secretProtector,
    ILogger<VoximplantScenarioKeyValidator> logger) : IVoximplantScenarioKeyValidator
{
    public async Task<bool> ValidateAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var providers = await context.CommunicationProviders
            .Where(p => p.ProviderType == "voximplant" && p.IsEnabled && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        VoximplantCallDataServiceLog.ValidatingScenarioApiKey(logger, apiKey[..Math.Min(8, apiKey.Length)] + "...", providers.Count);

        foreach (var provider in providers)
        {
            if (string.IsNullOrEmpty(provider.ConfigJson))
                continue;
            if (!JsonConfigMatchesScenarioKey(provider.ConfigJson, apiKey))
                continue;

            var storedPreview = TryGetScenarioKeyFromConfig(provider.ConfigJson) ?? "";
            VoximplantCallDataServiceLog.ComparingApiKeys(logger,
                storedPreview[..Math.Min(Math.Max(storedPreview.Length, 1), 8)] + "...",
                apiKey[..Math.Min(8, apiKey.Length)] + "...",
                true);
            return true;
        }

        return false;
    }

    private bool JsonConfigMatchesScenarioKey(string? configJson, string apiKey)
    {
        var stored = TryGetScenarioKeyFromConfig(configJson);
        if (string.IsNullOrEmpty(stored)) return false;

        var storedBytes = Encoding.UTF8.GetBytes(stored);
        var providedBytes = Encoding.UTF8.GetBytes(apiKey);
        return storedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(storedBytes, providedBytes);
    }

    private string? TryGetScenarioKeyFromConfig(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson))
            return null;
        try
        {
            var config = JsonSerializer.Deserialize<VoximplantConfigWithProvisioning>(configJson, VoximplantJsonOptions.Read);
            var stored = config?.Provisioning?.ScenarioApiKey;
            return string.IsNullOrEmpty(stored) ? null : secretProtector.Unprotect(stored);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
