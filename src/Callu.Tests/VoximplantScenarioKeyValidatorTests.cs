using System.Text.Json;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Voximplant;
using Callu.Infrastructure.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>
/// SEC-1 (highest-risk path): the scenario API key is stored ENCRYPTED in ConfigJson, but
/// VoxEngine presents the PLAINTEXT key in callbacks. The validator must decrypt the stored
/// value before its constant-time compare, or every voice callback would be wrongly rejected.
/// </summary>
public class VoximplantScenarioKeyValidatorTests
{
    private sealed class Factory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task Validates_Plaintext_Key_Against_Encrypted_Stored_Key()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scenariokey-{Guid.NewGuid():N}")
            .Options;

        var protector = new ProviderSecretProtector(
            new EphemeralDataProtectionProvider(),
            NullLogger<ProviderSecretProtector>.Instance);

        const string plainKey = "abc123def456abc123def456";
        var encrypted = protector.Protect(plainKey);
        Assert.StartsWith("enc:v1:", encrypted);

        var configJson = JsonSerializer.Serialize(new { provisioning = new { scenarioApiKey = encrypted } });

        await using (var seed = new ApplicationDbContext(options))
        {
            seed.CommunicationProviders.Add(new CommunicationProvider
            {
                Id = Guid.NewGuid(),
                Name = "vox",
                ProviderType = "voximplant",
                IsEnabled = true,
                ConfigJson = configJson,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var validator = new VoximplantScenarioKeyValidator(
            new Factory(options),
            protector,
            NullLogger<VoximplantScenarioKeyValidator>.Instance);

        Assert.True(await validator.ValidateAsync(plainKey));
        Assert.False(await validator.ValidateAsync("wrong-key"));
        Assert.False(await validator.ValidateAsync(""));
    }

    [Fact]
    public async Task Rejects_When_No_Provider_Configured()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scenariokey-empty-{Guid.NewGuid():N}")
            .Options;
        var protector = new ProviderSecretProtector(
            new EphemeralDataProtectionProvider(),
            NullLogger<ProviderSecretProtector>.Instance);

        var validator = new VoximplantScenarioKeyValidator(
            new Factory(options), protector, NullLogger<VoximplantScenarioKeyValidator>.Instance);

        Assert.False(await validator.ValidateAsync("any-key"));
    }
}
