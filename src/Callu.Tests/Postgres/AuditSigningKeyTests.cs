using Callu.Domain.Entities;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Callu.Tests;

/// <summary>Custody of the export signing keys, against the database that has to hold them.</summary>
// Key management is the substance of signing: the signature itself is a few lines. Losing the key
// ring means no new exports can be signed, and a second "active" key means the key an event was
// signed with is whichever row came back first.
[Collection(PostgresCollection.Name)]
public class AuditSigningKeyTests(PostgresFixture fixture)
{
    private static AuditSigningKeyService Service(ApplicationDbContext ctx, bool enabled = true, string ring = "signing") =>
        new(ctx,
            DataProtectionProvider.Create(ring),
            Options.Create(new AuditSigningOptions { Enabled = enabled }),
            NullLogger<AuditSigningKeyService>.Instance);

    private async Task<ApplicationDbContext> FreshAsync()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        return PostgresFixture.Context(cs);
    }

    [PostgresFact]
    public async Task MintsAKeyOnFirstUseAndReusesIt()
    {
        await using var ctx = await FreshAsync();

        var first = await Service(ctx).GetActiveSignerAsync();
        var second = await Service(ctx).GetActiveSignerAsync();

        Assert.NotNull(first);
        Assert.Equal(first!.KeyId, second!.KeyId);
        Assert.Equal(1, await ctx.AuditSigningKeys.CountAsync());
    }

    [PostgresFact]
    public async Task SignsNothingWhileTheFeatureIsOff()
    {
        await using var ctx = await FreshAsync();

        Assert.Null(await Service(ctx, enabled: false).GetActiveSignerAsync());
        Assert.Equal(0, await ctx.AuditSigningKeys.CountAsync());
    }

    [PostgresFact]
    public async Task RotationRetiresTheOldKeyWithoutDeletingIt()
    {
        await using var ctx = await FreshAsync();
        var before = (await Service(ctx).GetActiveSignerAsync())!.KeyId;

        var rotated = await Service(ctx).RotateAsync();

        Assert.NotEqual(before, rotated.KeyId);
        Assert.Equal(rotated.KeyId, (await Service(ctx).GetActiveSignerAsync())!.KeyId);

        var keys = await Service(ctx).GetPublicKeysAsync();
        Assert.Equal(2, keys.Count);
        Assert.NotNull(keys.Single(k => k.KeyId == before).RetiredAt);
        Assert.Null(keys.Single(k => k.KeyId == rotated.KeyId).RetiredAt);
    }

    /// <summary>Two rows claiming to be the active key is a data-model bug, so the database refuses it.</summary>
    [PostgresFact]
    public async Task TheDatabaseRefusesASecondActiveKey()
    {
        await using var ctx = await FreshAsync();
        await Service(ctx).GetActiveSignerAsync();

        ctx.AuditSigningKeys.Add(new AuditSigningKey
        {
            Id = Guid.NewGuid(),
            KeyId = "callu-audit-duplicate",
            PublicKey = "irrelevant",
            ProtectedPrivateKey = "irrelevant",
            CreatedAt = DateTime.UtcNow,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (error.InnerException as PostgresException)?.SqlState);
    }

    // Detection only: the key is not reminted, because a new one would silently stop older exports
    // from verifying while looking like everything recovered.
    [PostgresFact]
    public async Task AnUnreadableKeyRingLeavesExportsUnsignedRatherThanMintingANewKey()
    {
        await using var ctx = await FreshAsync();
        var original = (await Service(ctx, ring: "ring-a").GetActiveSignerAsync())!.KeyId;

        Assert.Null(await Service(ctx, ring: "ring-b").GetActiveSignerAsync());

        Assert.Equal(1, await ctx.AuditSigningKeys.CountAsync());
        Assert.Equal(original, (await Service(ctx, ring: "ring-a").GetActiveSignerAsync())!.KeyId);
    }

    [PostgresFact]
    public async Task NeverHandsOutPrivateKeyMaterial()
    {
        await using var ctx = await FreshAsync();
        await Service(ctx).GetActiveSignerAsync();

        var published = (await Service(ctx).GetPublicKeysAsync()).Single();
        var stored = await ctx.AuditSigningKeys.AsNoTracking().SingleAsync();

        Assert.Equal(stored.PublicKey, published.PublicKey);
        Assert.DoesNotContain(stored.ProtectedPrivateKey, published.PublicKey, StringComparison.Ordinal);
        Assert.Equal(32, Convert.FromBase64String(published.PublicKey).Length);
    }
}
