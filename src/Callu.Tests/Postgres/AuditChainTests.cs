using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Persistence;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>The tamper-evidence chain, against the database it has to survive.</summary>
// Sealing, pruning and verification all turn on ordering and on values round-tripping through
// Postgres, so an in-memory provider would prove nothing here.
[Collection(PostgresCollection.Name)]
public class AuditChainTests(PostgresFixture fixture)
{
    private static readonly DateTime Base = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    private static AuditLog Row(int minute, AuditAction action = AuditAction.Login) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = Base.AddMinutes(minute),
        UpdatedAt = Base.AddMinutes(minute),
        ActorId = $"user-{minute}",
        ActorDisplayName = "Ada",
        Action = action,
        ResourceType = "Incident",
        ResourceId = Guid.NewGuid(),
        Summary = $"entry {minute}",
    };

    private static AuditChainService Service(ApplicationDbContext ctx) =>
        new(ctx,
            DataProtectionProvider.Create(nameof(AuditChainTests)),
            NullLogger<AuditChainService>.Instance);

    private async Task<(AuditChainService Chain, ApplicationDbContext Ctx)> SeededAsync(int rows)
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var ctx = PostgresFixture.Context(cs);
        ctx.AddRange(Enumerable.Range(1, rows).Select(i => Row(i)));
        await ctx.SaveChangesAsync();

        return (Service(ctx), ctx);
    }

    // ---- sealing ----------------------------------------------------------

    [PostgresFact]
    public async Task EverySealedEntryLinksToTheOneBeforeIt()
    {
        var (chain, ctx) = await SeededAsync(5);

        var result = await chain.SealAsync();

        Assert.Equal(5, result.SealedCount);

        var sealed_ = await ctx.AuditLogs.OrderBy(l => l.Sequence).ToListAsync();
        Assert.Equal([1L, 2, 3, 4, 5], sealed_.Select(l => l.Sequence));
        Assert.Null(sealed_[0].PrevHash);
        for (var i = 1; i < sealed_.Count; i++)
            Assert.Equal(sealed_[i - 1].RowHash, sealed_[i].PrevHash);
    }

    [PostgresFact]
    public async Task ASecondRunOnlyChainsWhatArrivedSince()
    {
        var (chain, ctx) = await SeededAsync(3);
        await chain.SealAsync();

        ctx.AddRange(Row(10), Row(11));
        await ctx.SaveChangesAsync();

        var result = await chain.SealAsync();

        Assert.Equal(2, result.SealedCount);
        Assert.Equal(5, result.LastSequence);
        Assert.True((await chain.VerifyAsync()).Intact);
    }

    /// <summary>More entries than one batch, so the chain has to carry across the seam.</summary>
    [PostgresFact]
    public async Task ChainsCorrectlyAcrossBatchBoundaries()
    {
        var (chain, ctx) = await SeededAsync(AuditChainService.SealBatchSize + 20);

        var result = await chain.SealAsync();

        Assert.Equal(AuditChainService.SealBatchSize + 20, result.SealedCount);
        Assert.True((await chain.VerifyAsync()).Intact);
        Assert.Equal(0, await ctx.AuditLogs.CountAsync(l => l.Sequence == null));
    }

    // ---- what verification has to catch -----------------------------------

    [PostgresFact]
    public async Task VerifiesACleanChain()
    {
        var (chain, _) = await SeededAsync(4);
        await chain.SealAsync();

        var verdict = await chain.VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(4, verdict.CheckedCount);
        Assert.Null(verdict.FirstBrokenSequence);
    }

    [PostgresFact]
    public async Task CatchesAnEditedEntry()
    {
        var (chain, ctx) = await SeededAsync(4);
        await chain.SealAsync();

        await ctx.AuditLogs
            .Where(l => l.Sequence == 3)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Summary, "something else"));

        var verdict = await chain.VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(3, verdict.FirstBrokenSequence);
    }

    /// <summary>Changing who did it is the edit that matters most, and it is not the hashed field
    /// anyone would think of first.</summary>
    [PostgresFact]
    public async Task CatchesARewrittenActor()
    {
        var (chain, ctx) = await SeededAsync(3);
        await chain.SealAsync();

        await ctx.AuditLogs
            .Where(l => l.Sequence == 2)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ActorId, "somebody-else"));

        Assert.False((await chain.VerifyAsync()).Intact);
    }

    /// <summary>Rewriting the action is what a backfill would do, and the chain refuses it.</summary>
    // This is the executable form of "no migration ever touches AuditLogs": a row resealed from
    // Updated to Acknowledged reports as tampering on every install the next night.
    [PostgresFact]
    public async Task CatchesARewrittenAction()
    {
        var (chain, ctx) = await SeededAsync(3);
        await chain.SealAsync();

        await ctx.AuditLogs
            .Where(l => l.Sequence == 2)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Action, AuditAction.Acknowledged));

        Assert.False((await chain.VerifyAsync()).Intact);
    }

    [PostgresFact]
    public async Task CatchesADeletedEntryInTheMiddle()
    {
        var (chain, ctx) = await SeededAsync(5);
        await chain.SealAsync();

        await ctx.AuditLogs.Where(l => l.Sequence == 3).ExecuteDeleteAsync();

        var verdict = await chain.VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(4, verdict.FirstBrokenSequence);
    }

    /// <summary>Removing the newest entries leaves everything that remains internally consistent,
    /// which is exactly why the sealed tail is recorded separately.</summary>
    [PostgresFact]
    public async Task CatchesATruncatedTail()
    {
        var (chain, ctx) = await SeededAsync(5);
        await chain.SealAsync();

        await ctx.AuditLogs.Where(l => l.Sequence >= 4).ExecuteDeleteAsync();

        var verdict = await chain.VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(4, verdict.FirstBrokenSequence);
    }

    [PostgresFact]
    public async Task CatchesAnEntryWhoseHashWasCleared()
    {
        var (chain, ctx) = await SeededAsync(3);
        await chain.SealAsync();

        await ctx.AuditLogs
            .Where(l => l.Sequence == 2)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.RowHash, (string?)null));

        Assert.False((await chain.VerifyAsync()).Intact);
    }

    /// <summary>A row inserted straight into the table, with a sequence that looks plausible.</summary>
    [PostgresFact]
    public async Task CatchesAForgedEntrySplicedIntoTheChain()
    {
        var (chain, ctx) = await SeededAsync(4);
        await chain.SealAsync();

        var real = await ctx.AuditLogs.FirstAsync(l => l.Sequence == 3);
        await ctx.AuditLogs.Where(l => l.Sequence == 3).ExecuteDeleteAsync();

        var forged = Row(3, AuditAction.Deleted);
        forged.Sequence = 3;
        forged.PrevHash = real.PrevHash;
        forged.RowHash = real.RowHash;
        ctx.Add(forged);
        await ctx.SaveChangesAsync();

        Assert.False((await chain.VerifyAsync()).Intact);
    }

    // ---- retention ---------------------------------------------------------

    /// <summary>Retention deleting the oldest entries is legitimate and must not read as tampering.</summary>
    [PostgresFact]
    public async Task APrunedPrefixStillVerifies()
    {
        var (chain, ctx) = await SeededAsync(6);
        await chain.SealAsync();

        await ctx.AuditLogs.Where(l => l.Sequence <= 2).ExecuteDeleteAsync();
        await chain.NotePrunedAsync();

        var verdict = await chain.VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(4, verdict.CheckedCount);
    }

    /// <summary>Deleting the oldest entries without going through retention still has to show up.</summary>
    [PostgresFact]
    public async Task AnUnrecordedPrefixDeletionIsStillABreak()
    {
        var (chain, ctx) = await SeededAsync(6);
        await chain.SealAsync();

        await ctx.AuditLogs.Where(l => l.Sequence <= 2).ExecuteDeleteAsync();

        Assert.False((await chain.VerifyAsync()).Intact);
    }

    /// <summary>After a prune, an edit inside what survived is still caught.</summary>
    [PostgresFact]
    public async Task StillCatchesAnEditAfterAPrune()
    {
        var (chain, ctx) = await SeededAsync(6);
        await chain.SealAsync();

        await ctx.AuditLogs.Where(l => l.Sequence <= 2).ExecuteDeleteAsync();
        await chain.NotePrunedAsync();

        await ctx.AuditLogs
            .Where(l => l.Sequence == 5)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Summary, "edited after the prune"));

        var verdict = await chain.VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(5, verdict.FirstBrokenSequence);
    }

    // ---- the limits we state out loud --------------------------------------

    /// <summary>Entries written before the chain existed stay unsealed until the job reaches them;
    /// an empty table is not a broken chain.</summary>
    [PostgresFact]
    public async Task AnEmptyChainIsIntact()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var verdict = await Service(PostgresFixture.Context(cs)).VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(0, verdict.CheckedCount);
    }

    /// <summary>Unsealed entries are outside the chain, so they neither verify nor break it.</summary>
    [PostgresFact]
    public async Task EntriesNotYetSealedDoNotBreakVerification()
    {
        var (chain, ctx) = await SeededAsync(3);
        await chain.SealAsync();

        ctx.AddRange(Row(20), Row(21));
        await ctx.SaveChangesAsync();

        var verdict = await chain.VerifyAsync();

        Assert.True(verdict.Intact);
        Assert.Equal(3, verdict.CheckedCount);
    }

    /// <summary>The key is generated once and reused, or nothing sealed earlier would verify.</summary>
    [PostgresFact]
    public async Task TheKeyIsCreatedOnceAndKept()
    {
        var (chain, ctx) = await SeededAsync(2);
        await chain.SealAsync();

        var first = await ctx.AuditChainStates.AsNoTracking().SingleAsync();

        ctx.Add(Row(30));
        await ctx.SaveChangesAsync();
        await chain.SealAsync();

        var second = await ctx.AuditChainStates.AsNoTracking().SingleAsync();

        Assert.Equal(first.ProtectedKey, second.ProtectedKey);
        Assert.True((await chain.VerifyAsync()).Intact);
    }

    // ---- Data Protection key ring lost / mismatched ------------------------

    /// <summary>
    /// A restored database without the matching dp-keys volume cannot decrypt ProtectedKey.
    /// Detection only: never mint a new key that would "heal" an unverifiable history.
    /// </summary>
    [PostgresFact]
    public async Task ForeignKeyRing_VerifyReportsBroken_WithoutReminting()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var ctx = PostgresFixture.Context(cs);
        ctx.AddRange(Enumerable.Range(1, 3).Select(i => Row(i)));
        await ctx.SaveChangesAsync();

        var original = new AuditChainService(
            ctx, DataProtectionProvider.Create("ring-original"), NullLogger<AuditChainService>.Instance);
        await original.SealAsync();

        var protectedKey = (await ctx.AuditChainStates.AsNoTracking().SingleAsync()).ProtectedKey;

        var foreign = new AuditChainService(
            ctx, DataProtectionProvider.Create("ring-foreign"), NullLogger<AuditChainService>.Instance);

        var verdict = await foreign.VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(0, verdict.CheckedCount);
        Assert.Null(verdict.FirstBrokenSequence);
        Assert.Equal(AuditChainService.KeyUnreadableReason, verdict.Reason);
        Assert.Equal(protectedKey, (await ctx.AuditChainStates.AsNoTracking().SingleAsync()).ProtectedKey);
    }

    [PostgresFact]
    public async Task ForeignKeyRing_SealSkipsAndLeavesProtectedKeyAlone()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var ctx = PostgresFixture.Context(cs);
        ctx.AddRange(Enumerable.Range(1, 2).Select(i => Row(i)));
        await ctx.SaveChangesAsync();

        var original = new AuditChainService(
            ctx, DataProtectionProvider.Create("seal-ring-a"), NullLogger<AuditChainService>.Instance);
        await original.SealAsync();

        ctx.Add(Row(40));
        await ctx.SaveChangesAsync();
        var protectedKey = (await ctx.AuditChainStates.AsNoTracking().SingleAsync()).ProtectedKey;
        var sealedBefore = await ctx.AuditLogs.CountAsync(l => l.Sequence != null);

        var foreign = new AuditChainService(
            ctx, DataProtectionProvider.Create("seal-ring-b"), NullLogger<AuditChainService>.Instance);

        var result = await foreign.SealAsync();

        Assert.Equal(0, result.SealedCount);
        Assert.Equal(AuditChainService.KeyUnreadableReason, result.FailureReason);
        Assert.Equal(protectedKey, (await ctx.AuditChainStates.AsNoTracking().SingleAsync()).ProtectedKey);
        Assert.Equal(sealedBefore, await ctx.AuditLogs.CountAsync(l => l.Sequence != null));
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(l => l.Sequence == null));
    }

    // ---- canonical form ----------------------------------------------------

    /// <summary>The canonical form of a row read back from Postgres, against the one sealed in memory.</summary>
    // The timestamp goes into the hash as a formatted string, so a column that gave back an
    // unspecified Kind would drop the trailing Z and make every stored hash unverifiable on read.
    [PostgresFact]
    public async Task ARoundTrippedRowProducesTheSameCanonicalForm()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var written = Row(1);
        // Sub-second precision is where a narrowing round-trip would show up first.
        written.CreatedAt = new DateTime(2026, 8, 5, 9, 12, 33, 456, DateTimeKind.Utc).AddTicks(7891);

        var writeContext = PostgresFixture.Context(cs);
        writeContext.Add(written);
        await writeContext.SaveChangesAsync();

        var read = await PostgresFixture.Context(cs).AuditLogs.AsNoTracking().SingleAsync();

        Assert.Equal(DateTimeKind.Utc, read.CreatedAt.Kind);
        Assert.Equal(
            AuditChainHash.Canonical(written, sequence: 1, prevHash: null, CalluAuditChain.CanonicalizationId),
            AuditChainHash.Canonical(read, sequence: 1, prevHash: null, CalluAuditChain.CanonicalizationId));
    }

    /// <summary>Sealing from a tracked entity, then verifying against what the table actually holds.</summary>
    // DateTime.UtcNow is where real rows get their timestamp, and it is rarely aligned to the
    // microsecond the column stores.
    [PostgresFact]
    public async Task ARowSealedBeforeItsTimestampRoundTripsStillVerifies()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var ctx = PostgresFixture.Context(cs);
        var row = Row(1);
        row.CreatedAt = new DateTime(2026, 8, 5, 9, 12, 33, 456, DateTimeKind.Utc).AddTicks(7891);
        ctx.Add(row);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, (await Service(ctx).SealAsync()).SealedCount);

        Assert.True((await Service(PostgresFixture.Context(cs)).VerifyAsync()).Intact);
    }
}
